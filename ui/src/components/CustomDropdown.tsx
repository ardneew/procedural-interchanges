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
        style={{ color: selectedOption ? "#f8fafc" : "#94a3b8" }}
        onClick={() => setIsOpen(!isOpen)}
      >
        <span>{selectedOption ? selectedOption.label : placeholder}</span>
        <span
          style={{
            fontSize: "8rem",
            color: "#38bdf8",
            transform: isOpen ? "rotate(180deg)" : "none",
            transition: "transform 0.15s ease",
          }}
        >
          ▼
        </span>
      </div>

      {isOpen && (
        <div className={styles.dropdownMenu}>
          {options.map((opt) => (
            <div
              key={`opt-${opt.value}`}
              className={`${styles.dropdownOption} ${opt.value === value ? styles.activeOption : ""}`}
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
