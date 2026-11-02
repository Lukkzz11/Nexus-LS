import { useEffect, useCallback } from "react";
import { cn } from "../../utils/cn";
import { X } from "lucide-react";

export function Modal({
  isOpen,
  onClose,
  children,
  size = "md",
  className,
  closeOnEsc = true,
  closeOnBackdrop = true
}) {
  const handleKeyDown = useCallback(
    (e) => {
      if (closeOnEsc && e.key === "Escape") {
        onClose();
      }
    },
    [closeOnEsc, onClose]
  );

  useEffect(() => {
    if (isOpen) {
      document.body.style.overflow = "hidden";
      window.addEventListener("keydown", handleKeyDown);
    } else {
      document.body.style.overflow = "unset";
    }

    return () => {
      document.body.style.overflow = "unset";
      window.removeEventListener("keydown", handleKeyDown);
    };
  }, [isOpen, handleKeyDown]);

  if (!isOpen) return null;

  const sizeClasses = {
    sm: "max-w-md",
    md: "max-w-lg",
    lg: "max-w-2xl",
    xl: "max-w-4xl",
    full: "max-w-[95vw] h-[90vh]"
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-end sm:items-center justify-center p-0 sm:p-6 overflow-y-auto"
      role="dialog"
      aria-modal="true"
    >
      {/* Backdrop */}
      <div
        className="fixed inset-0 bg-slate-950/75 transition-opacity"
        onClick={closeOnBackdrop ? onClose : undefined}
      />

      {/* Modal Surface */}
      <div
        className={cn(
          "relative w-full z-10 bg-slate-900 border border-slate-800 shadow-2xl rounded-t-2xl sm:rounded-xl flex flex-col max-h-[92vh] sm:max-h-[90vh] overflow-hidden text-slate-100 animate-in fade-in slide-in-from-bottom-4 sm:slide-in-from-bottom-0 sm:zoom-in-95 duration-150",
          // Quando o conteúdo vem dentro de um <form>, ele também precisa ser flex para o corpo rolar
          // e o rodapé (botões Salvar/Cancelar) continuar visível
          "[&>form]:flex [&>form]:flex-col [&>form]:flex-1 [&>form]:min-h-0",
          sizeClasses[size] || sizeClasses.md,
          className
        )}
      >
        {children}
      </div>
    </div>
  );
}

export function ModalHeader({ className, title, description, onClose, children }) {
  return (
    <div
      className={cn(
        "shrink-0 flex items-start justify-between p-5 border-b border-slate-800/80 bg-slate-950/50",
        className
      )}
    >
      <div className="space-y-1">
        {title && <h3 className="text-lg font-semibold text-white">{title}</h3>}
        {description && <p className="text-xs text-slate-400">{description}</p>}
        {children}
      </div>

      {onClose && (
        <button
          type="button"
          onClick={onClose}
          className="text-slate-400 hover:text-white p-1.5 -m-1 rounded-lg hover:bg-slate-800 transition-colors cursor-pointer"
          aria-label="Fechar"
        >
          <X className="w-5 h-5" />
        </button>
      )}
    </div>
  );
}

export function ModalBody({ className, children }) {
  return (
    <div className={cn("p-6 flex-1 min-h-0 overflow-y-auto space-y-4", className)}>
      {children}
    </div>
  );
}

export function ModalFooter({ className, children }) {
  return (
    <div
      className={cn(
        "shrink-0 flex items-center justify-end gap-3 p-4 border-t border-slate-800/80 bg-slate-950/50",
        className
      )}
    >
      {children}
    </div>
  );
}

export default Modal;
