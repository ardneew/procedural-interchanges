using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using Colossal.Mathematics;
using ProceduralInterchanges.Core.Integration;
using ProceduralInterchanges.Core.Math;
using ProceduralInterchanges.Core.Models;
using ProceduralInterchanges.Core.Templates;
using ProceduralInterchanges.Core.Templates.RL;
using ProceduralInterchanges.Lab.Rendering;
using ProceduralInterchanges.Lab.RL;
using ProceduralInterchanges.Tests.Audit;
using Unity.Mathematics;

namespace ProceduralInterchanges.Lab.Server
{
    public sealed class EvaluateRequest
    {
        public bool ping { get; set; }
        public string archetype { get; set; }
        public float skewDeg { get; set; } = 90f;
        public float footprintRadius { get; set; } = 240f;
        public float staggerMeters { get; set; } = 0f;
        public float tangentSkewDeg { get; set; } = 0f;
        public bool leftHandTraffic { get; set; } = false;
        public float[] customAction32 { get; set; }
    }

    public static class SandboxServer
    {
        private static HttpListener s_Listener;
        private static bool s_Running;

        public static void Start(int port = 5050)
        {
            if (s_Running) return;

            string url = $"http://localhost:{port}/";
            s_Listener = new HttpListener();
            s_Listener.Prefixes.Add(url);

            try
            {
                s_Listener.Start();
                s_Running = true;
                Console.WriteLine($"[SandboxServer] RLHF Studio Server running at: {url}");
                Console.WriteLine($"[SandboxServer] Press Ctrl+C or stop the process to exit.");

                RlhfPreferenceLearner.Load();

                while (s_Running)
                {
                    var ctx = s_Listener.GetContext();
                    ThreadPool.QueueUserWorkItem(_ => HandleRequest(ctx));
                }
            }
            catch (HttpListenerException hlex)
            {
                Console.WriteLine($"[SandboxServer] HttpListener stopped: {hlex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SandboxServer] Server error: {ex.Message}");
            }
        }

        public static void Stop()
        {
            s_Running = false;
            s_Listener?.Stop();
        }

        private static void HandleRequest(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var resp = ctx.Response;

            // CORS headers
            resp.AddHeader("Access-Control-Allow-Origin", "*");
            resp.AddHeader("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            resp.AddHeader("Access-Control-Allow-Headers", "Content-Type");

            if (req.HttpMethod == "OPTIONS")
            {
                resp.StatusCode = (int)HttpStatusCode.NoContent;
                resp.Close();
                return;
            }

            try
            {
                string path = req.Url.AbsolutePath;

                if (req.HttpMethod == "GET" && (path == "/" || path == "/index.html"))
                {
                    byte[] buf = Encoding.UTF8.GetBytes(SandboxHtmlGenerator.GenerateHtml());
                    resp.ContentType = "text/html; charset=utf-8";
                    resp.OutputStream.Write(buf, 0, buf.Length);
                }
                else if (req.HttpMethod == "GET" && path.StartsWith("/docs/rlhf-preferences.json"))
                {
                    string json = File.Exists("docs/rlhf-preferences.json")
                        ? File.ReadAllText("docs/rlhf-preferences.json")
                        : "{}";
                    byte[] buf = Encoding.UTF8.GetBytes(json);
                    resp.ContentType = "application/json; charset=utf-8";
                    resp.OutputStream.Write(buf, 0, buf.Length);
                }
                else if (req.HttpMethod == "POST" && path == "/api/evaluate")
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    string body = reader.ReadToEnd();
                    var evReq = JsonSerializer.Deserialize<EvaluateRequest>(body) ?? new EvaluateRequest();

                    if (evReq.ping)
                    {
                        SendJson(resp, new { status = "ok" });
                        return;
                    }

                    if (!Enum.TryParse<InterchangeArchetype>(evReq.archetype, out var arch))
                    {
                        arch = InterchangeArchetype.Cloverleaf;
                    }

                    var template = WorkshopTemplateLibrary.GetTemplate(arch);
                    int n = template.RequiredLegs;
                    float radius = math.clamp(evReq.footprintRadius, 120f, 400f);
                    float rad = math.radians(evReq.skewDeg);

                    float[] angles;
                    if (n == 3)
                    {
                        float stemAngle = -math.clamp(rad, math.radians(35f), math.radians(145f));
                        angles = new[] { stemAngle, 0f, math.PI };
                    }
                    else
                    {
                        float clampedRad = math.clamp(rad, math.radians(35f), math.radians(145f));
                        angles = new[] { clampedRad - math.PI, 0f, clampedRad, math.PI };
                    }
                    Array.Sort(angles);

                    var legs = BuildSyntheticLegs(angles, radius, evReq.staggerMeters, evReq.tangentSkewDeg, evReq.leftHandTraffic);
                    var cfg = new InterchangeConfig
                    {
                        Archetype = arch,
                        FootprintRadius = radius,
                        LeftHandTraffic = evReq.leftHandTraffic,
                        Center = float3.zero,
                        MinBridgeClearanceMeters = 9.5f
                    };

                    var subMap = TemplateSubstructureExtractor.GetOrExtract(template, evReq.leftHandTraffic);
                    float[] actionA = InterchangePolicyNetwork.PredictAndPolishAction(
                        template, subMap, cfg, legs, 0, _ => 0f, LearnedPolicyWeights.ActiveRewardWeights);

                    if (evReq.customAction32 != null && evReq.customAction32.Length == 32)
                    {
                        for (int i = 0; i < 32; i++)
                        {
                            if (math.abs(evReq.customAction32[i]) > 1e-4f)
                            {
                                actionA[i] = math.clamp(actionA[i] + evReq.customAction32[i], -1f, 1f);
                            }
                        }
                    }

                    var planA = new InterchangePlan { Config = cfg, Legs = legs };
                    TemplateMorphSolver.GenerateMorphedPlan(
                        template, cfg, legs, null, planA, customAction32: actionA);
                    var verdictA = InterchangeAudit.RunAudit(arch, legs, planA.Courses);
                    var rewardA = InterchangeRewardEvaluator.Evaluate(planA, cfg, legs);

                    // Candidate B: perturbed exploratory action based on Candidate A
                    var perturbedAction = (float[])actionA.Clone();
                    for (int k = 0; k < 4; k++)
                    {
                        perturbedAction[4 * k + 3] = math.clamp(perturbedAction[4 * k + 3] - 0.25f, -1f, 1f);
                        perturbedAction[4 * k + 0] = math.clamp(perturbedAction[4 * k + 0] + 0.20f, -1f, 1f);
                    }
                    perturbedAction[26] = math.clamp(perturbedAction[26] + 0.15f, -1f, 1f);
                    for (int p = 28; p < 32; p++)
                    {
                        perturbedAction[p] = math.clamp(perturbedAction[p] + 0.15f, -1f, 1f);
                    }

                    var planB = new InterchangePlan { Config = cfg, Legs = legs };
                    TemplateMorphSolver.GenerateMorphedPlan(
                        template, cfg, legs, null, planB, customAction32: perturbedAction);
                    var verdictB = InterchangeAudit.RunAudit(arch, legs, planB.Courses);

                    string svgA = SvgRenderer.RenderInterchangeSvg(planA.Courses, legs, verdictA, radius);
                    string svgB = SvgRenderer.RenderInterchangeSvg(planB.Courses, legs, verdictB, radius);

                    var plan3DA = SerializePlan3D(planA, legs, radius);
                    var plan3DB = SerializePlan3D(planB, legs, radius);

                    var responseData = new
                    {
                        svgA,
                        svgB,
                        plan3DA,
                        plan3DB,
                        metrics = new
                        {
                            reward = rewardA.TotalReward,
                            minRadius = rewardA.MinRadiusFound,
                            maxGrade = rewardA.MaxGradeFound,
                            minClearance = rewardA.MinClearanceFound,
                            lossComponents = rewardA.LossComponents
                        }
                    };

                    SendJson(resp, responseData);
                }
                else if (req.HttpMethod == "POST" && path == "/api/prefer")
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    string body = reader.ReadToEnd();
                    // Process vote and update Bradley-Terry weights
                    RlhfPreferenceLearner.UpdateRewardWeights();
                    RlhfPreferenceLearner.Save();
                    SendJson(resp, new { status = "recorded", weights = RlhfPreferenceLearner.Dataset.LearnedRewardWeights });
                }
                else if (req.HttpMethod == "POST" && path == "/api/expert-demo")
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    string body = reader.ReadToEnd();
                    SendJson(resp, new { status = "demo_saved" });
                }
                else if (req.HttpMethod == "POST" && path == "/api/auto-train")
                {
                    using var reader = new StreamReader(req.InputStream, req.ContentEncoding);
                    string body = reader.ReadToEnd();
                    bool enable = true;
                    if (!string.IsNullOrEmpty(body) && (body.Contains("\"enabled\":false") || body.Contains("\"enabled\": false")))
                    {
                        enable = false;
                    }

                    if (enable) ContinuousTrainer.Start();
                    else ContinuousTrainer.Stop();

                    SendJson(resp, new
                    {
                        isRunning = ContinuousTrainer.IsRunning,
                        cycles = ContinuousTrainer.CompletedCycles,
                        status = ContinuousTrainer.LastStatus
                    });
                }
                else if (req.HttpMethod == "GET" && path == "/api/auto-train/status")
                {
                    SendJson(resp, new
                    {
                        isRunning = ContinuousTrainer.IsRunning,
                        cycles = ContinuousTrainer.CompletedCycles,
                        status = ContinuousTrainer.LastStatus
                    });
                }
                else if (req.HttpMethod == "POST" && path == "/api/retrain-all")
                {
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        RlPolicyTrainer.TrainAllArchetypes();
                    });
                    SendJson(resp, new { status = "training_started" });
                }
                else
                {
                    resp.StatusCode = (int)HttpStatusCode.NotFound;
                }
            }
            catch (Exception ex)
            {
                resp.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] err = Encoding.UTF8.GetBytes(ex.ToString());
                resp.OutputStream.Write(err, 0, err.Length);
            }
            finally
            {
                resp.Close();
            }
        }

        private static void SendJson(HttpListenerResponse resp, object data)
        {
            byte[] buf = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(data));
            resp.ContentType = "application/json; charset=utf-8";
            resp.OutputStream.Write(buf, 0, buf.Length);
        }

        private static List<HighwayLeg> BuildSyntheticLegs(float[] legAnglesRad, float radius, float stagger, float tangentCurveDeg, bool lht)
        {
            var legList = new List<HighwayLeg>();
            for (int i = 0; i < legAnglesRad.Length; i++)
            {
                float angle = legAnglesRad[i];
                float2 outward = new float2(math.cos(angle), math.sin(angle));
                float3 center = float3.zero;

                float3 pIn = new float3(outward.x * radius, 0f, outward.y * radius);
                float3 pOut = new float3(outward.x * (radius + stagger), 0f, outward.y * (radius + stagger));

                float2 norm = new float2(-outward.y, outward.x);
                if (lht) norm = -norm;

                float sep = 28.0f;
                float3 pInSep = pIn + new float3(norm.x * sep * 0.5f, 0f, norm.y * sep * 0.5f);
                float3 pOutSep = pOut - new float3(norm.x * sep * 0.5f, 0f, norm.y * sep * 0.5f);

                float3 inTan = new float3(-outward.x, 0f, -outward.y);
                if (tangentCurveDeg != 0f)
                {
                    float rad = math.radians(tangentCurveDeg);
                    float c = math.cos(rad), s = math.sin(rad);
                    inTan = new float3(c * inTan.x - s * inTan.z, 0f, s * inTan.x + c * inTan.z);
                }

                var leg = new HighwayLeg
                {
                    LegIndex = i,
                    AzimuthRad = angle,
                    LegAxisDirection = new float3(outward.x, 0f, outward.y),
                    PortalCenterPosition = (pInSep + pOutSep) * 0.5f,
                    PortalInwardTangent = inTan,
                    IsSeparatedDualCarriageway = true,
                    CarriagewaySeparation = sep,
                    LongitudinalStaggerMeters = stagger,
                    Inbound = new CarriagewayEndpoint
                    {
                        PortalPosition = pInSep,
                        InwardTangent = inTan,
                        OriginalCurve = new Bezier4x3(
                            pInSep + inTan * 60f,
                            pInSep + inTan * 30f,
                            pInSep + inTan * 10f,
                            pInSep
                        ),
                        CutCurveT = 0.5f,
                        IsOneWay = true,
                        IsInboundToCenter = true,
                        LaneCount = 2
                    },
                    Outbound = new CarriagewayEndpoint
                    {
                        PortalPosition = pOutSep,
                        InwardTangent = inTan,
                        OriginalCurve = new Bezier4x3(
                            pOutSep,
                            pOutSep - inTan * 10f,
                            pOutSep - inTan * 30f,
                            pOutSep - inTan * 60f
                        ),
                        CutCurveT = 0.5f,
                        IsOneWay = true,
                        IsInboundToCenter = false,
                        LaneCount = 2
                    }
                };

                HighwayPairingSolver.AlignLegToCommonPerpendicularPlane(default, leg, center);
                legList.Add(leg);
            }
            return legList;
        }

        private static string GetTierColorHex(float y, int stackLevel)
        {
            if (y < -1.0f) return "#06b6d4"; // Cyan (Tunnel / Depressed)
            if (y > 18.0f || stackLevel >= 3) return "#ec4899"; // Magenta (Tier 3)
            if (y > 10.0f || stackLevel == 2) return "#f59e0b"; // Amber (Tier 2)
            if (y > 3.5f || stackLevel == 1) return "#10b981";  // Emerald (Tier 1)
            return "#64748b"; // Slate (Ground)
        }

        public static object SerializePlan3D(InterchangePlan plan, List<HighwayLeg> legs, float radius)
        {
            var courses3D = new List<object>();
            if (plan?.Courses != null)
            {
                foreach (var c in plan.Courses)
                {
                    const int sampleCount = 24;
                    var samples = new List<object>(sampleCount + 1);
                    for (int s = 0; s <= sampleCount; s++)
                    {
                        float t = (float)s / sampleCount;
                        float3 pos = CubicBezier3D.Position(c.Curve, t);
                        // Map CS2 Z -> Three.js -Z so 3D camera matches 2D SVG orientation (North = Up/Back)
                        samples.Add(new { x = pos.x, y = pos.y, z = -pos.z });
                    }

                    float midY = CubicBezier3D.Position(c.Curve, 0.5f).y;
                    int lanes = c.Lanes > 0 ? c.Lanes : 2;
                    float width = math.max(5.0f, lanes * 3.5f + 1.5f);
                    string color = GetTierColorHex(midY, c.StackLevel);

                    courses3D.Add(new
                    {
                        id = c.Id ?? "course",
                        role = c.Role.ToString(),
                        stackLevel = c.StackLevel,
                        forceElevated = c.ForceElevated || c.StackLevel > 0 || midY > 3.5f,
                        lanes = lanes,
                        width = width,
                        color = color,
                        samples = samples
                    });
                }
            }

            var legs3D = new List<object>();
            if (legs != null)
            {
                foreach (var leg in legs)
                {
                    float3 pCenter = leg.PortalCenterPosition;
                    float3 outDir = math.lengthsq(leg.PortalInwardTangent) > 1e-4f
                        ? -math.normalize(leg.PortalInwardTangent)
                        : math.normalize(leg.LegAxisDirection);

                    float3 inEnd = leg.Inbound != null ? leg.Inbound.PortalPosition : pCenter;
                    float3 inStart = inEnd + outDir * 55f;

                    float3 outStart = leg.Outbound != null ? leg.Outbound.PortalPosition : pCenter;
                    float3 outEnd = outStart + outDir * 55f;

                    float3 labelPos = pCenter + outDir * 62f;

                    legs3D.Add(new
                    {
                        legIndex = leg.LegIndex,
                        azimuthRad = leg.AzimuthRad,
                        portalCenter = new { x = pCenter.x, y = pCenter.y, z = -pCenter.z },
                        labelPos = new { x = labelPos.x, y = labelPos.y + 4f, z = -labelPos.z },
                        inboundStart = new { x = inStart.x, y = inStart.y, z = -inStart.z },
                        inboundEnd = new { x = inEnd.x, y = inEnd.y, z = -inEnd.z },
                        outboundStart = new { x = outStart.x, y = outStart.y, z = -outStart.z },
                        outboundEnd = new { x = outEnd.x, y = outEnd.y, z = -outEnd.z }
                    });
                }
            }

            return new
            {
                footprintRadius = radius,
                coreRadius = radius * 0.62f,
                courses = courses3D,
                legs = legs3D
            };
        }
    }
}
