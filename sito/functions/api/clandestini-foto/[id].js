import { preparaClandestini, uguali } from "../../../lib/clandestini.js";

// GET /api/clandestini-foto/<id> : la foto di un clandestino.
// Pubblica solo se il clandestino è approvato; con ?chiave=STATS_KEY anche quelle in attesa (pagina di approvazione).

const nonTrovata = () => new Response("Not found", { status: 404 });

export async function onRequestGet({ request, env, params }) {
  const id = Number(params.id);
  if (!env.VISITE || !Number.isInteger(id) || id <= 0) return nonTrovata();
  const chiave = new URL(request.url).searchParams.get("chiave") || "";
  const admin = !!env.STATS_KEY && uguali(chiave, env.STATS_KEY);
  await preparaClandestini(env.VISITE);
  const r = await env.VISITE.prepare(
    `SELECT f.tipo, f.dati, c.stato FROM clandestini_foto f JOIN clandestini c ON c.id = f.id WHERE f.id = ?1`).bind(id).first();
  if (!r || (r.stato !== "ok" && !admin)) return nonTrovata();
  return new Response(new Uint8Array(r.dati), {
    headers: {
      "Content-Type": r.tipo,
      "Cache-Control": admin ? "no-store" : "public, max-age=3600",
      "X-Content-Type-Options": "nosniff",
    },
  });
}
