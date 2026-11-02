import { forwardRef } from "react";
import { cn } from "../../utils/cn";
import { Loader2 } from "lucide-react";

// Hierarquia: primary = a ação principal da tela (uma por região); secondary/outline = apoio;
// ghost = ações discretas; danger = destrutivas; success = confirmações finais (ex.: finalizar venda).
const buttonVariants = {
  primary:
    "bg-sky-500 hover:bg-sky-400 text-white font-semibold border border-transparent",
  secondary:
    "bg-slate-800 hover:bg-slate-700 text-slate-100 font-medium border border-slate-700",
  outline:
    "bg-transparent hover:bg-slate-800/60 text-slate-200 font-medium border border-slate-700 hover:border-slate-600",
  ghost:
    "bg-transparent hover:bg-slate-800/50 text-slate-300 hover:text-slate-100 font-medium border border-transparent",
  danger:
    "bg-rose-600 hover:bg-rose-500 text-white font-semibold border border-transparent",
  warning:
    "bg-amber-500/15 hover:bg-amber-500/25 text-amber-300 font-semibold border border-amber-500/40",
  success:
    "bg-emerald-500 hover:bg-emerald-400 text-slate-950 font-semibold border border-transparent"
};

// Em telas de toque (tablet/celular) os alvos têm no mínimo 40 px
const buttonSizes = {
  sm: "h-8 px-3 text-[13px] rounded-md gap-1.5 pointer-coarse:min-h-10",
  md: "h-10 px-4 text-sm rounded-lg gap-2 pointer-coarse:min-h-11",
  lg: "h-12 px-6 text-base rounded-xl gap-2.5",
  icon: "h-10 w-10 p-0 rounded-lg justify-center pointer-coarse:h-11 pointer-coarse:w-11",
  iconSm: "h-8 w-8 p-0 rounded-md justify-center pointer-coarse:h-10 pointer-coarse:w-10"
};

export const Button = forwardRef(
  (
    {
      className,
      variant = "primary",
      size = "md",
      isLoading = false,
      disabled = false,
      leftIcon,
      rightIcon,
      children,
      type = "button",
      ...props
    },
    ref
  ) => {
    const isActuallyDisabled = disabled || isLoading;

    return (
      <button
        ref={ref}
        type={type}
        disabled={isActuallyDisabled}
        className={cn(
          "inline-flex items-center justify-center whitespace-nowrap transition-colors duration-150 select-none cursor-pointer outline-none focus-visible:ring-2 focus-visible:ring-sky-400 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-950",
          buttonVariants[variant] || buttonVariants.primary,
          buttonSizes[size] || buttonSizes.md,
          isActuallyDisabled && "opacity-50 cursor-not-allowed pointer-events-none",
          className
        )}
        aria-busy={isLoading}
        {...props}
      >
        {isLoading ? (
          <>
            <Loader2 className="w-4 h-4 animate-spin text-current shrink-0" />
            {children && <span>{children}</span>}
          </>
        ) : (
          <>
            {leftIcon && <span className="shrink-0">{leftIcon}</span>}
            {children}
            {rightIcon && <span className="shrink-0">{rightIcon}</span>}
          </>
        )}
      </button>
    );
  }
);

Button.displayName = "Button";
export default Button;
