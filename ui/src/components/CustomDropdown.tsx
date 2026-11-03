import React, { useState, useRef } from "react";
import styles from "./studio.module.scss";

export interface DropdownOption {
  value: string | number;
  label: string;
}

interface CustomDropdownProps {
  value: string | number;
  options: DropdownOption[];
  onChange: (value: any) => void;
  placeholder?: string;
  style?: React.CSSProperties;
}

export const CustomDropdown: React.FC<CustomDropdownProps> = ({
  value,
  options,
  onChange,
  placeholder = "Select...",
  style,
}) => {
  const [isOpen, setIsOpen] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  const selectedOption = options.find((o) => o.value === value);

  return (
    <div
      ref={containerRef}
      style={{
        position: "relative",
        userSelect: "none",
        ...style,
      }}
    >
      <div
        className={styles.dropdownHeader}
        style={{
          background: "rgba(15, 23, 42, 0.8)",
          border: "1rem solid rgba(255, 255, 255, 0.15)",
          borderRadius: "4rem",
          padding: "4rem 8rem",
          fontSize: "10rem",
          color: selectedOption ? "#f8fafc" : "#94a3b8",
          cursor: "pointer",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center",
        }}
        onClick={() => setIsOpen(!isOpen)}
      >
        <span>{selectedOption ? selectedOption.label : placeholder}</span>
        <span style={{ fontSize: "8rem", transform: isOpen ? "rotate(180deg)" : "none", transition: "transform 0.15s" }}>
          ▼
        </span>
      </div>

      {isOpen && (
        <div
          className={styles.dropdownMenu}
          style={{
            position: "absolute",
            top: "100%",
            left: 0,
            right: 0,
            marginTop: "2rem",
            background: "#0f172a",
            border: "1rem solid rgba(0, 229, 255, 0.3)",
            borderRadius: "4rem",
            maxHeight: "140rem",
            overflowY: "auto",
            zIndex: 9999,
            boxShadow: "0 4rem 12rem rgba(0, 0, 0, 0.5)",
          }}
        >
          {options.map((opt) => (
            <div
              key={`opt-${opt.value}`}
              style={{
                padding: "4rem 8rem",
                fontSize: "10rem",
                color: opt.value === value ? "#00e5ff" : "#cbd5e1",
                background: opt.value === value ? "rgba(0, 229, 255, 0.1)" : "transparent",
                cursor: "pointer",
              }}
              onMouseEnter={(e) => (e.currentTarget.style.background = "rgba(255, 255, 255, 0.08)")}
              onMouseLeave={(e) =>
                (e.currentTarget.style.background = opt.value === value ? "rgba(0, 229, 255, 0.1)" : "transparent")
              }
              onClick={() => {
                onChange(opt.value);
                setIsOpen(false);
              }}
            >
              {opt.label}
            </div>
          ))}
        </div>
      )}
    </div>
  );
};
