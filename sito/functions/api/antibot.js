import { antibotAttivo } from "../../lib/antibot.js";

// GET /api/antibot : la Site key pubblica di Turnstile se il controllo è acceso, altrimenti vuota.
// La legge sito/antibot.js per decidere se mostrare il riquadro di verifica nei moduli.
export function onRequestGet({ env }) {
  return new Response(JSON.stringify({ sitekey: antibotAttivo(env) ? env.TURNSTILE_SITEKEY : "" }), {
    headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "public, max-age=300" },
  });
}
