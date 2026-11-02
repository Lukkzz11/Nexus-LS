import { forwardRef } from "react";
import { cn } from "../../utils/cn";

export const Input = forwardRef(
  (
    {
      className,
      type = "text",
      label,
      error,
      helperText,
      leftIcon,
      rightIcon,
      size = "md",
      required = false,
      id,
      ...props
    },
    ref
  ) => {
    const inputId = id || (label ? label.toLowerCase().replace(/\s+/g, "-") : undefined);

    const sizeClasses = {
      sm: "h-8 px-2.5 text-[13px] rounded-md pointer-coarse:h-10",
      md: "h-10 px-3.5 text-sm rounded-lg pointer-coarse:h-11",
      lg: "h-12 px-4 text-base rounded-xl"
    };

    return (
      <div className="w-full flex flex-col gap-1.5">
        {label && (
          <label
            htmlFor={inputId}
            className="text-[13px] font-medium text-slate-300 flex items-center justify-between"
          >
            <span>
              {label}
              {required && <span className="text-rose-400 ml-1">*</span>}
            </span>
          </label>
        )}

        <div className="relative flex items-center w-full">
          {leftIcon && (
            <div className="absolute left-3 flex items-center pointer-events-none text-slate-400">
              {leftIcon}
            </div>
          )}

          <input
            id={inputId}
            ref={ref}
            type={type}
            required={required}
            className={cn(
              "w-full bg-slate-950/80 text-white placeholder-slate-500 border border-slate-700/80 transition-colors duration-150 outline-none",
              "focus:border-sky-500 focus:ring-2 focus:ring-sky-500/20",
              "disabled:bg-slate-900 disabled:text-slate-500 disabled:border-slate-800 disabled:cursor-not-allowed",
              sizeClasses[size] || sizeClasses.md,
              leftIcon && "pl-9",
              rightIcon && "pr-9",
              error && "border-rose-500/80 focus:border-rose-500 focus:ring-rose-500/20",
              className
            )}
            {...props}
          />

          {rightIcon && (
            <div className="absolute right-3 flex items-center text-slate-400">
              {rightIcon}
            </div>
          )}
        </div>

        {error ? (
          <span className="text-xs text-rose-400 font-medium">{error}</span>
        ) : helperText ? (
          <span className="text-xs text-slate-400">{helperText}</span>
        ) : null}
      </div>
    );
  }
);

Input.displayName = "Input";
export default Input;
