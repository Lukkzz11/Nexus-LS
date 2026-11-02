/**
 * Utilitários gerais e formatadores para o PDV
 */

export function formatCurrency(value) {
  const number = Number(value) || 0;
  return new Intl.NumberFormat("pt-BR", {
    style: "currency",
    currency: "BRL"
  }).format(number);
}

export function formatDate(date) {
  if (!date) return "";
  const d = date?.toDate ? date.toDate() : new Date(date);
  return new Intl.DateTimeFormat("pt-BR", {
    dateStyle: "short",
    timeStyle: "short"
  }).format(d);
}
