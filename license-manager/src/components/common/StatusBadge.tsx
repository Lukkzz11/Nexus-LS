import React from "react";
import { StatusLicenca } from "../../types";

interface StatusBadgeProps {
  status: StatusLicenca;
  size?: "sm" | "md" | "lg";
}

export const StatusBadge: React.FC<StatusBadgeProps> = ({ status, size = "md" }) => {
  const configs: Record<
    StatusLicenca,
    { label: string; bg: string; text: string; dot: string; border: string }
  > = {
    ATIVA: {
      label: "Ativa",
      bg: "bg-emerald-50",
      text: "text-emerald-700",
      dot: "bg-emerald-500",
      border: "border-emerald-200",
    },
    VENCENDO: {
      label: "Vencendo",
      bg: "bg-amber-50",
      text: "text-amber-800",
      dot: "bg-amber-500",
      border: "border-amber-200",
    },
    VENCIDA: {
      label: "Vencida",
      bg: "bg-red-50",
      text: "text-red-700",
      dot: "bg-red-500",
      border: "border-red-200",
    },
    BLOQUEADA: {
      label: "Bloqueada",
      bg: "bg-rose-50",
      text: "text-rose-700",
      dot: "bg-rose-500",
      border: "border-rose-200",
    },
    CANCELADA: {
      label: "Cancelada",
      bg: "bg-slate-100",
      text: "text-slate-600",
      dot: "bg-slate-400",
      border: "border-slate-200",
    },
    PENDENTE: {
      label: "Pendente",
      bg: "bg-sky-50",
      text: "text-sky-700",
      dot: "bg-sky-500",
      border: "border-sky-200",
    },
  };

  const current = configs[status] || configs.ATIVA;
  const sizeClasses =
    size === "sm"
      ? "text-xs px-2 py-0.5"
      : size === "lg"
      ? "text-sm px-3 py-1 font-medium"
      : "text-xs px-2.5 py-1 font-medium";

  return (
    <span
      className={`inline-flex items-center gap-1.5 rounded-full border ${current.bg} ${current.text} ${current.border} ${sizeClasses} select-none`}
    >
      <span className={`w-1.5 h-1.5 rounded-full ${current.dot}`} />
      {current.label}
    </span>
  );
};
