import { clsx } from "clsx";
import { twMerge } from "tailwind-merge";

/**
 * Combina classes Tailwind de forma segura e sem conflitos de especificidade
 */
export function cn(...inputs) {
  return twMerge(clsx(inputs));
}
