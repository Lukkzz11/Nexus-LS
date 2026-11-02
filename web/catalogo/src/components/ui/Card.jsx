import { forwardRef } from "react";
import { cn } from "../../utils/cn";

export const Card = forwardRef(
  ({ className, variant = "default", style, children, ...props }, ref) => {
    const variants = {
      default:
        "card-themed border shadow-sm rounded-xl transition-colors duration-200",
      interactive:
        "card-themed border shadow-sm rounded-xl hover:border-sky-500/50 transition-colors duration-150 cursor-pointer",
      subtle:
        "card-themed border rounded-xl transition-colors duration-200",
      flat:
        "card-themed border rounded-xl transition-colors duration-200"
    };

    return (
      <div
        ref={ref}
        className={cn(variants[variant] || variants.default, className)}
        style={style}
        {...props}
      >
        {children}
      </div>
    );
  }
);
Card.displayName = "Card";

export const CardHeader = forwardRef(({ className, style, children, ...props }, ref) => (
  <div
    ref={ref}
    className={cn("flex flex-col space-y-1.5 p-5 border-b border-slate-800/60", className)}
    style={{ borderColor: "var(--border-subtle, rgba(255, 255, 255, 0.08))", ...style }}
    {...props}
  >
    {children}
  </div>
));
CardHeader.displayName = "CardHeader";

export const CardTitle = forwardRef(({ className, style, children, ...props }, ref) => (
  <h3
    ref={ref}
    className={cn("text-lg font-semibold text-white", className)}
    style={{ color: "var(--text-heading, #ffffff)", ...style }}
    {...props}
  >
    {children}
  </h3>
));
CardTitle.displayName = "CardTitle";

export const CardDescription = forwardRef(({ className, style, children, ...props }, ref) => (
  <p
    ref={ref}
    className={cn("text-xs text-slate-400 font-normal", className)}
    style={style}
    {...props}
  >
    {children}
  </p>
));
CardDescription.displayName = "CardDescription";

export const CardContent = forwardRef(({ className, style, children, ...props }, ref) => (
  <div ref={ref} className={cn("p-5", className)} style={style} {...props}>
    {children}
  </div>
));
CardContent.displayName = "CardContent";

export const CardFooter = forwardRef(({ className, style, children, ...props }, ref) => (
  <div
    ref={ref}
    className={cn("flex items-center p-5 pt-0 border-t border-slate-800/60 mt-4", className)}
    style={{ borderColor: "var(--border-subtle, rgba(255, 255, 255, 0.08))", ...style }}
    {...props}
  >
    {children}
  </div>
));
CardFooter.displayName = "CardFooter";

export default Card;
