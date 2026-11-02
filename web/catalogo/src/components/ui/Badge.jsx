import { forwardRef } from "react";
import { cn } from "../../utils/cn";

const badgeVariants = {
  success: "bg-emerald-500/10 text-emerald-400 border-emerald-500/25",
  warning: "bg-amber-500/10 text-amber-400 border-amber-500/25",
  amber: "bg-amber-500/10 text-amber-400 border-amber-500/25",
  danger: "bg-rose-500/10 text-rose-400 border-rose-500/25",
  info: "bg-sky-500/10 text-sky-400 border-sky-500/25",
  purple: "bg-violet-500/10 text-violet-300 border-violet-500/25",
  indigo: "bg-indigo-500/10 text-indigo-300 border-indigo-500/25",
  pink: "bg-pink-500/10 text-pink-300 border-pink-500/25",
  subtle: "bg-white/[0.04] text-slate-300 border-white/10",
  neutral: "bg-slate-800 text-slate-300 border-slate-700",
  outline: "bg-transparent text-slate-300 border-slate-600"
};

const dotColors = {
  success: "bg-emerald-400",
  warning: "bg-amber-400",
  amber: "bg-amber-400",
  danger: "bg-rose-400",
  info: "bg-sky-400",
  purple: "bg-violet-400",
  indigo: "bg-indigo-400",
  pink: "bg-pink-400",
  subtle: "bg-slate-400",
  neutral: "bg-slate-400",
  outline: "bg-slate-400"
};

// Selos em caixa normal (sem CAIXA ALTA): o estado é lido pela cor e pelo texto, não pelo grito.
export const Badge = forwardRef(
  (
    {
      className,
      variant = "neutral",
      size = "md",
      withDot = false,
      pulseDot = false,
      children,
      ...props
    },
    ref
  ) => {
    const sizeClasses = {
      sm: "text-[11px] px-2 py-0.5 gap-1",
      md: "text-xs px-2.5 py-1 gap-1.5"
    };

    return (
      <span
        ref={ref}
        className={cn(
          "inline-flex items-center font-semibold rounded-full border select-none whitespace-nowrap",
          badgeVariants[variant] || badgeVariants.neutral,
          sizeClasses[size] || sizeClasses.md,
          className
        )}
        {...props}
      >
        {withDot && (
          <span
            className={cn(
              "w-1.5 h-1.5 rounded-full shrink-0",
              dotColors[variant] || dotColors.neutral,
              pulseDot && "animate-pulse"
            )}
          />
        )}
        {children}
      </span>
    );
  }
);

Badge.displayName = "Badge";
export default Badge;
