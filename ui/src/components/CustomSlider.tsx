import React, { useRef, useCallback } from "react";
import styles from "./studio.module.scss";

interface CustomSliderProps {
  label: string;
  min: number;
  max: number;
  step?: number;
  value: number;
  unit?: string;
  decimals?: number;
  onChange: (val: number) => void;
}

export const CustomSlider: React.FC<CustomSliderProps> = ({
  label,
  min,
  max,
  step = 1,
  value,
  unit = "",
  decimals = 0,
  onChange,
}) => {
  const trackRef = useRef<HTMLDivElement>(null);

  const calculateValueFromPointer = useCallback(
    (clientX: number) => {
      if (!trackRef.current) return value;
      const rect = trackRef.current.getBoundingClientRect();
      const rawFrac = (clientX - rect.left) / Math.max(1, rect.width);
      const clampedFrac = Math.max(0, Math.min(1, rawFrac));
      let rawVal = min + clampedFrac * (max - min);

      if (step > 0) {
        rawVal = Math.round((rawVal - min) / step) * step + min;
      }
      return Math.max(min, Math.min(max, rawVal));
    },
    [min, max, step, value]
  );

  const handlePointerDown = (e: React.PointerEvent) => {
    e.preventDefault();
    const target = e.currentTarget as HTMLElement;
    target.setPointerCapture(e.pointerId);

    const newVal = calculateValueFromPointer(e.clientX);
    onChange(newVal);

    const handlePointerMove = (moveEv: PointerEvent) => {
      const v = calculateValueFromPointer(moveEv.clientX);
      onChange(v);
    };

    const handlePointerUp = () => {
      window.removeEventListener("pointermove", handlePointerMove);
      window.removeEventListener("pointerup", handlePointerUp);
    };

    window.addEventListener("pointermove", handlePointerMove);
    window.addEventListener("pointerup", handlePointerUp);
  };

  const percentage = Math.max(0, Math.min(100, ((value - min) / Math.max(1e-4, max - min)) * 100));

  return (
    <div className={styles.controlRow}>
      <span className={styles.label}>{label}</span>
      <div
        ref={trackRef}
        className={styles.sliderTrack}
        onPointerDown={handlePointerDown}
        style={{ cursor: "pointer" }}
      >
        <div className={styles.sliderFill} style={{ width: `${percentage}%` }} />
        <div className={styles.sliderThumb} style={{ left: `${percentage}%` }} />
      </div>
      <span className={styles.value}>
        {value.toFixed(decimals)}
        {unit}
      </span>
    </div>
  );
};
