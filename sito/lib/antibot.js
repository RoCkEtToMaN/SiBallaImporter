// Controllo anti-bot Turnstile di Cloudflare (gratuito) per i moduli del sito: clandestini, annunci, messaggi.
// Si accende da solo quando nel progetto Pages ci sono ENTRAMBE le impostazioni:
//   TURNSTILE_SITEKEY  (variabile, la "Site key" pubblica)  -> la pagina mostra il riquadro di verifica
//   TURNSTILE_SECRET   (secret, la "Secret key")            -> il server controlla la verifica
// Con una sola delle due resta spento: così i moduli non si bloccano mai per una configurazione a metà.

export function antibotAttivo(env) {
  return !!(env.TURNSTILE_SECRET && env.TURNSTILE_SITEKEY);
}

export async function antibotOk(env, token, ip) {
  if (!antibotAttivo(env)) return true;
  if (!token) return false;
  const body = new FormData();
  body.append("secret", env.TURNSTILE_SECRET);
  body.append("response", String(token));
  if (ip) body.append("remoteip", ip);
  try {
    const r = await fetch("https://challenges.cloudflare.com/turnstile/v0/siteverify", { method: "POST", body });
    return (await r.json()).success === true;
  } catch { return false; }
}
