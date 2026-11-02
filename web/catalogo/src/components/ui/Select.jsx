import { forwardRef } from "react";
import { cn } from "../../utils/cn";
import { ChevronDown } from "lucide-react";

export const Select = forwardRef(
  (
    {
      className,
      label,
      error,
      helperText,
      options = [],
      placeholder = "Selecione uma opção...",
      id,
      required = false,
      children,
      ...props
    },
    ref
  ) => {
    const selectId = id || (label ? label.toLowerCase().replace(/\s+/g, "-") : undefined);

    return (
      <div className="w-full flex flex-col gap-1.5">
        {label && (
          <label
            htmlFor={selectId}
            className="text-[13px] font-medium text-slate-300 flex items-center justify-between"
          >
            <span>
              {label}
              {required && <span className="text-rose-400 ml-1">*</span>}
            </span>
          </label>
        )}

        <div className="relative flex items-center w-full">
          <select
            id={selectId}
            ref={ref}
            required={required}
            className={cn(
              "w-full h-10 pointer-coarse:h-11 appearance-none bg-slate-950/80 text-white border border-slate-700/80 rounded-lg px-3.5 pr-10 text-sm transition-colors duration-150 outline-none",
              "focus:border-sky-500 focus:ring-2 focus:ring-sky-500/20",
              "disabled:bg-slate-900 disabled:text-slate-500 disabled:cursor-not-allowed",
              error && "border-rose-500/80 focus:border-rose-500 focus:ring-rose-500/20",
              className
            )}
            {...props}
          >
            {placeholder && (
              <option value="" disabled className="bg-slate-900 text-slate-500">
                {placeholder}
              </option>
            )}
            {options.length > 0
              ? options.map((opt) => (
                  <option
                    key={opt.value}
                    value={opt.value}
                    className="bg-slate-900 text-white py-1"
                  >
                    {opt.label}
                  </option>
                ))
              : children}
          </select>

          <div className="absolute right-3 pointer-events-none text-slate-400 flex items-center">
            <ChevronDown className="w-4 h-4" />
          </div>
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

Select.displayName = "Select";
export default Select;
