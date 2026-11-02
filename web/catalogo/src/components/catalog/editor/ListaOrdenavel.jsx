import { DndContext, KeyboardSensor, PointerSensor, TouchSensor, closestCenter, useSensor, useSensors } from "@dnd-kit/core";
import { SortableContext, arrayMove, sortableKeyboardCoordinates, useSortable, verticalListSortingStrategy } from "@dnd-kit/sortable";
import { CSS } from "@dnd-kit/utilities";
import { GripVertical } from "lucide-react";
import { cn } from "../../../utils/cn";

/**
 * Lista que se reordena arrastando (mouse, toque e teclado) com @dnd-kit (MIT, mesmo usado no ZPLab).
 * O resultado sai por `onReordenar(novaListaDeIds)`; as setas "subir/descer" de cada tela continuam existindo.
 * Teclado: Tab até a alça, Espaço para pegar, setas para mover, Espaço para soltar, Esc para cancelar.
 */
export function ListaOrdenavel({ ids, onReordenar, rotulo, className, children }) {
  const sensores = useSensors(
    useSensor(PointerSensor, { activationConstraint: { distance: 6 } }),
    useSensor(TouchSensor, { activationConstraint: { delay: 180, tolerance: 6 } }),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates })
  );

  const aoSoltar = ({ active, over }) => {
    if (!over || active.id === over.id) return;
    const de = ids.indexOf(active.id);
    const para = ids.indexOf(over.id);
    if (de < 0 || para < 0) return;
    onReordenar(arrayMove(ids, de, para));
  };

  return (
    <DndContext sensors={sensores} collisionDetection={closestCenter} onDragEnd={aoSoltar}>
      <SortableContext items={ids} strategy={verticalListSortingStrategy}>
        <ol className={className} aria-label={rotulo}>
          {children}
        </ol>
      </SortableContext>
    </DndContext>
  );
}

/** Item da lista: `children` recebe a alça de arrastar para o posicionar onde quiser. */
export function ItemOrdenavel({ id, rotuloAlca, className, children }) {
  const { attributes, listeners, setNodeRef, setActivatorNodeRef, transform, transition, isDragging } = useSortable({ id });
  const alca = (
    <button
      type="button"
      ref={setActivatorNodeRef}
      aria-label={rotuloAlca}
      title="Arraste para reordenar"
      className="grid h-7 w-6 shrink-0 cursor-grab touch-none place-items-center rounded text-slate-500 hover:text-slate-200 active:cursor-grabbing"
      {...attributes}
      {...listeners}
    >
      <GripVertical className="h-4 w-4" aria-hidden="true" />
    </button>
  );
  return (
    <li
      ref={setNodeRef}
      style={{ transform: CSS.Transform.toString(transform), transition }}
      className={cn(className, isDragging && "relative z-10 opacity-80 shadow-lg ring-2 ring-sky-400")}
    >
      {typeof children === "function" ? children(alca) : children}
    </li>
  );
}
