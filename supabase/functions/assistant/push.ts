// Push-сповіщення на телефони через Firebase Cloud Messaging (HTTP v1).
// Секрет FIREBASE_SERVICE_ACCOUNT — JSON закритого ключа сервісного акаунта Firebase.
// Без нього функція просто не надсилає push (телефон тоді бачить повідомлення під час синхронізації).

import type postgres from "npm:postgres@3.4.9";

type Sql = ReturnType<typeof postgres>;

interface ServiceAccount {
  project_id: string;
  client_email: string;
  private_key: string;
}

let cachedToken: { value: string; expiresAt: number } | null = null;

function serviceAccount(): ServiceAccount | null {
  const raw = Deno.env.get("FIREBASE_SERVICE_ACCOUNT");
  if (!raw) return null;
  return JSON.parse(raw) as ServiceAccount;
}

// Повідомлення Коуча → push на телефони цього пристрою-адресата (або всі, якщо адресата немає).
// Помилки push не мають ламати роботу асистента, тож лише пишемо їх у лог.
export async function pushMessage(sql: Sql, message: { id: number; kind: string; text: string; target: string | null }) {
  const account = serviceAccount();
  if (!account) return;

  try {
    const tokens = message.target
      ? await sql`select device_id, token from public.ft_push_tokens where device_id = ${message.target}`
      : await sql`select device_id, token from public.ft_push_tokens`;
    if (tokens.length === 0) return;

    const accessToken = await googleAccessToken(account);
    for (const row of tokens) {
      const response = await fetch(`https://fcm.googleapis.com/v1/projects/${account.project_id}/messages:send`, {
        method: "POST",
        headers: { "Authorization": `Bearer ${accessToken}`, "Content-Type": "application/json" },
        body: JSON.stringify({
          message: {
            token: row.token,
            // Лише дані: сповіщення показує сам застосунок (свій канал, відкриття чату)
            data: { message_id: String(message.id), kind: message.kind, text: message.text },
            android: { priority: "high" },
          },
        }),
      });
      if (response.ok) continue;

      const error = await response.text();
      // Застосунок видалено або токен застарів — більше на нього не шлемо
      if (response.status === 404 || error.includes("UNREGISTERED")) {
        await sql`delete from public.ft_push_tokens where device_id = ${row.device_id}`;
      } else {
        console.error(`FCM ${response.status}: ${error}`);
      }
    }
  } catch (e) {
    console.error("push failed", e);
  }
}

// OAuth-токен Google для FCM: JWT, підписаний закритим ключем сервісного акаунта (RS256)
async function googleAccessToken(account: ServiceAccount): Promise<string> {
  if (cachedToken && cachedToken.expiresAt > Date.now() + 60_000) return cachedToken.value;

  const now = Math.floor(Date.now() / 1000);
  const header = base64url(JSON.stringify({ alg: "RS256", typ: "JWT" }));
  const claims = base64url(JSON.stringify({
    iss: account.client_email,
    scope: "https://www.googleapis.com/auth/firebase.messaging",
    aud: "https://oauth2.googleapis.com/token",
    iat: now,
    exp: now + 3600,
  }));
  const key = await crypto.subtle.importKey(
    "pkcs8",
    pemToDer(account.private_key),
    { name: "RSASSA-PKCS1-v1_5", hash: "SHA-256" },
    false,
    ["sign"],
  );
  const signature = await crypto.subtle.sign("RSASSA-PKCS1-v1_5", key, new TextEncoder().encode(`${header}.${claims}`));
  const jwt = `${header}.${claims}.${base64url(new Uint8Array(signature))}`;

  const response = await fetch("https://oauth2.googleapis.com/token", {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({ grant_type: "urn:ietf:params:oauth:grant-type:jwt-bearer", assertion: jwt }),
  });
  if (!response.ok) throw new Error(`Google OAuth ${response.status}: ${await response.text()}`);
  const json = await response.json();
  cachedToken = { value: json.access_token, expiresAt: Date.now() + json.expires_in * 1000 };
  return cachedToken.value;
}

function pemToDer(pem: string): ArrayBuffer {
  const base64 = pem.replace(/-----[^-]+-----/g, "").replace(/\s+/g, "");
  const bytes = Uint8Array.from(atob(base64), (c) => c.charCodeAt(0));
  return bytes.buffer;
}

function base64url(input: string | Uint8Array): string {
  const bytes = typeof input === "string" ? new TextEncoder().encode(input) : input;
  let binary = "";
  for (const b of bytes) binary += String.fromCharCode(b);
  return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}
