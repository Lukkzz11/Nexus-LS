import { useState } from "react";

/**
 * Quantidade que também aceita digitação (útil no atacado: "120" sem clicar 120 vezes).
 * Enquanto digita, aceita vazio; ao sair do campo volta para um valor válido entre `min` e `max`.
 */
export default function CampoQuantidade({ value, onChange, min = 1, max = 999, className = "", style, ariaLabel = "Quantidade" }) {
  const [texto, setTexto] = useState(null); // null = mostra o valor vindo de fora

  const limitar = (n) => Math.max(min, Math.min(max, Math.floor(Number(n) || min)));

  return (
    <input
      type="text"
      inputMode="numeric"
      pattern="[0-9]*"
      aria-label={ariaLabel}
      value={texto ?? String(value)}
      onFocus={(e) => e.target.select()}
      onChange={(e) => {
        const digitos = e.target.value.replace(/\D/g, "").slice(0, String(max).length);
        setTexto(digitos);
        if (digitos !== "") onChange(limitar(digitos));
      }}
      onBlur={() => {
        setTexto(null);
        onChange(limitar(texto === null || texto === "" ? value : texto));
      }}
      onKeyDown={(e) => {
        if (e.key === "Enter") e.currentTarget.blur();
      }}
      className={className}
      style={style}
    />
  );
}
