// Хмарний «мозок» асистента FocusTracker (Supabase Edge Function, Deno).
//
// Будять її сама база (розклад і тригер застосунок налаштовує сам):
//   {"reason":"tick"} — pg_cron раз на 5 хвилин: ранковий план, перевірки, вечірній підсумок;
//   {"reason":"chat"} — тригер на нове повідомлення користувача в ft_assistant_messages.
// Виклик без правильного заголовка x-ft-secret (ft_assistant_state.cron_secret) відхиляється.
//
// Секрети функції: ANTHROPIC_API_KEY (задає користувач), SUPABASE_DB_URL (дає Supabase),
// FIREBASE_SERVICE_ACCOUNT (необов'язковий: push на телефони, див. push.ts).

import Anthropic from "npm:@anthropic-ai/sdk@0.128.0";
import type {
  BetaMessage,
  BetaMessageParam,
  BetaTextBlockParam,
  BetaTool,
  BetaToolResultBlockParam,
  MessageCreateParamsNonStreaming,
} from "npm:@anthropic-ai/sdk@0.128.0/resources/beta/messages/messages";
import postgres from "npm:postgres@3.4.9";
import { pushMessage } from "./push.ts";

// Від імені асистента пишуться задачі в ft_entities (updated_by). Пристрої завантажують
// рядки з updated_by ≠ свого id, тож задача з'являється на всіх.
const CLOUD_ASSISTANT_ID = "00000000-0000-4000-8000-0000a5515700";
const FOCUS_TRACKER_APP = "focustracker.app";
const MAX_FOCUS_MINUTES = 180;
const ACTIVE_WINDOW_MINUTES = 10;
const MEMORY_MAX_CHARS = 4000;
const MAX_TOOL_ROUNDS = 6;

const sql = postgres(Deno.env.get("SUPABASE_DB_URL")!, { max: 3, prepare: false });
const anthropic = new Anthropic(); // ключ — з ANTHROPIC_API_KEY

type Kind = "check" | "plan" | "review" | "chat";

interface Settings {
  goals: string;
  rules: string;
  fromHour: number;
  toHour: number;
  checkMinutes: number;
  mayStartFocus: boolean;
  model: string;
  enabled: boolean;
  timeZone: string;
  name: string;
}

interface Presence {
  device_id: string;
  name: string;
  last_input_at: Date | null;
  foreground_app: string | null;
}

// ===================== HTTP =====================

Deno.serve(async (req) => {
  if (req.method !== "POST") return new Response("method not allowed", { status: 405 });

  const [state] = await sql`select cron_secret::text as secret from public.ft_assistant_state where id = 1`;
  const secret = req.headers.get("x-ft-secret") ?? "";
  if (!state || !safeEqual(secret, state.secret)) return new Response("unauthorized", { status: 401 });

  const body = await req.json().catch(() => ({}));
  const reason = body?.reason;
  if (reason !== "tick" && reason !== "chat") return new Response("unknown reason", { status: 400 });

  try {
    const result = reason === "tick" ? await tick() : await chatRequest();
    await sql`update public.ft_assistant_state set last_run_at = now() where id = 1`;
    return Response.json({ ok: true, result });
  } catch (e) {
    await logError(e);
    return Response.json({ ok: false, error: errorText(e) }, { status: 500 });
  }
});

function safeEqual(a: string, b: string): boolean {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}

function errorText(e: unknown): string {
  if (e instanceof Anthropic.APIError) return `Claude API ${e.status ?? ""}: ${e.message}`;
  return e instanceof Error ? e.message : String(e);
}

async function logError(e: unknown) {
  console.error(e);
  try {
    await sql`update public.ft_assistant_state set last_error = ${errorText(e)}, last_run_at = now() where id = 1`;
  } catch (inner) {
    console.error(inner);
  }
}

// ===================== Черга =====================
// Одночасно працює лише один виклик (оренда busy_until у ft_assistant_state).
// Хто тримає оренду, перед звільненням відповідає на всі нові повідомлення чату,
// тож виклик чату, що не отримав оренди, може спокійно завершитись.

async function acquire(): Promise<boolean> {
  const rows = await sql`
    update public.ft_assistant_state set busy_until = now() + interval '170 seconds'
    where id = 1 and (busy_until is null or busy_until < now())
    returning id`;
  return rows.length > 0;
}

async function release() {
  await sql`update public.ft_assistant_state set busy_until = null where id = 1`;
}

async function hasPendingChat(): Promise<boolean> {
  const rows = await sql`select 1 from public.ft_assistant_messages where status = 'pending' limit 1`;
  return rows.length > 0;
}

// Виконує work під орендою, потім відповідає на повідомлення, що прийшли тим часом
async function exclusive<T>(work: () => Promise<T>): Promise<T | "busy"> {
  if (!(await acquire())) return "busy";
  let result: T;
  try {
    result = await work();
  } finally {
    // Навіть якщо перевірка впала, на питання в чаті відповідаємо
    try {
      await answerPendingChat();
    } finally {
      await release();
    }
  }
  // Повідомлення могло прийти, поки ми звільняли оренду, а його виклик уже пішов ні з чим
  while (await hasPendingChat()) {
    if (!(await acquire())) break;
    try {
      await answerPendingChat();
    } finally {
      await release();
    }
  }
  return result;
}

// ===================== tick =====================

async function tick(): Promise<string> {
  const outcome = await exclusive(async () => {
    await sql`delete from public.ft_recent_activity where at < now() - interval '3 hours'`;

    const settings = await loadSettings();
    if (!settings?.enabled) return "disabled";

    const now = new Date();
    const local = zoned(now, settings.timeZone);
    const productive = local.hour >= settings.fromHour && local.hour < settings.toHour;
    const [state] = await sql`
      select plan_date::text as plan_date, review_date::text as review_date, last_check_at
      from public.ft_assistant_state where id = 1`;
    const active = await activeDevice();

    // Як і локальний асистент: план і перевірки — лише коли користувач за комп'ютером
    if (productive && active && state.plan_date !== local.date) {
      await sql`update public.ft_assistant_state set plan_date = ${local.date}::date, last_check_at = now() where id = 1`;
      await run("plan", settings, active);
      return "plan";
    }
    const sinceCheck = state.last_check_at ? (now.getTime() - new Date(state.last_check_at).getTime()) / 60000 : Infinity;
    if (productive && active && sinceCheck >= settings.checkMinutes - 1) {
      await sql`update public.ft_assistant_state set last_check_at = now() where id = 1`;
      await run("check", settings, active);
      return "check";
    }
    if (!productive && local.hour >= settings.toHour && state.plan_date === local.date && state.review_date !== local.date) {
      await sql`update public.ft_assistant_state set review_date = ${local.date}::date where id = 1`;
      await run("review", settings, active);
      return "review";
    }
    return "idle";
  });
  return String(outcome);
}

// ===================== chat =====================

async function chatRequest(): Promise<string> {
  const outcome = await exclusive(() => Promise.resolve("chat"));
  return String(outcome);
}

async function answerPendingChat() {
  const pending = await sql`
    select id from public.ft_assistant_messages where status = 'pending' order by id`;
  if (pending.length === 0) return;
  const ids = pending.map((p) => p.id as string);

  try {
    const settings = await loadSettings();
    if (!settings?.enabled) throw new Error("Асистент у хмарі вимкнений у налаштуваннях застосунку.");
    await run("chat", settings, await activeDevice());
    await sql`update public.ft_assistant_messages set status = 'done', error = null where id in ${sql(ids)}`;
  } catch (e) {
    await logError(e);
    await sql`update public.ft_assistant_messages set status = 'error', error = ${errorText(e)} where id in ${sql(ids)}`;
  }
}

// ===================== Модель =====================

function systemPrompt(name: string): string {
  return `Ти — ${name}, особистий асистент продуктивності користувача в застосунку FocusTracker.
Ти бачиш, які програми й вікна він використовує на своїх пристроях (ПК, ноутбук), його цілі з пріоритетами,
задачі та розклад. Твоє завдання — допомагати йому робити те, що наближає до цілей,
і помічати прокрастинацію, зокрема «продуктивну»: корисну на вигляд роботу, яка зараз
не є пріоритетом (наприклад, пиляти власний пет-проєкт, коли ціль №1 — відгуки на вакансії).

Як діяти:
- Оцінюй поведінку відносно цілей і їхнього порядку. Відпочинок і особистий час поза
  продуктивними годинами — нормально, не втручайся, якщо правила користувача не кажуть інакше.
- Не набридай. Якщо все добре або ти нещодавно вже казав про те саме, а ситуація не змінилась
  суттєво — нічого не роби (не викликай жодного інструмента). Про ту саму річ — не частіше ніж раз на ~40 хвилин.
- Будь коротким (1–3 речення), конкретним і по-дружньому прямим; трохи гумору доречно.
  Пропонуй один конкретний наступний крок на 15–30 хвилин.
- Наростай поступово, дивлячись на свої попередні повідомлення: м'яко нагадати (nudge) → спитати,
  що він робить і навіщо (ask) → запропонувати конкретний план (plan) → фокус-режим (start_focus).
- Фокус-режим — лише якщо його дозволено, а користувач ігнорує попередні нагадування або
  цього вимагає його правило. Блокуй тільки явно відволікаючі програми (ігри, Steam, Discord тощо),
  вказуючи ідентифікатори процесів у дужках із контексту. Ніколи не блокуй FocusTracker,
  а браузер і робочі інструменти — якщо вони потрібні для цілей.
- Фокус-режим і задачі діють на всіх пристроях одразу. На телефоні програми звуться так, як у статистиці
  телефона (YouTube, Instagram, TikTok…): щоб заблокувати їх і там, додай ці назви до списку apps.
- Задачу додавай (add_task), лише коли це справді допоможе: користувач попросив або план дня
  містить конкретний крок, якого ще немає серед відкритих задач.
- Важливі довгострокові спостереження про користувача (що працює, що ні, домовленості)
  зберігай через remember — вони будуть у твоїй пам'яті наступними днями.
- Пиши тією мовою, якою користувач написав свої цілі.`;
}

function profile(s: Settings, memory: string): string {
  return `## Цілі користувача (за пріоритетом, його словами)
${s.goals?.trim() || "(не задані)"}

## Правила користувача для тебе
${s.rules?.trim() || "(немає)"}

## Розклад
Продуктивні години: ${pad(s.fromHour)}:00–${pad(s.toHour)}:00 (${s.timeZone}). Поза ними — особистий час.
Фокус-режим вмикати ${s.mayStartFocus ? "дозволено" : "НЕ дозволено, лише пропонувати"}.

## Твоя пам'ять із попередніх днів
${memory.trim() || "(поки порожня)"}`;
}

const TASKS: Record<Exclude<Kind, "chat">, string> = {
  plan: "Почалися продуктивні години. Коротко привітайся і запропонуй план на сьогодні: 2–4 конкретні кроки під цілі з урахуванням задач. Надішли його через send_message з kind = plan.",
  review: "Продуктивні години закінчились. Чесно й коротко підсумуй день: що зроблено для цілей, що ні, і одна порада на завтра. Надішли через send_message з kind = review. Потім збережи через remember 1–3 короткі спостереження, корисні на майбутнє (якщо є що зберегти).",
  check: "Оціни, що відбувається, і виріши, чи потрібно втрутитися зараз. Якщо ні — не викликай жодного інструмента.",
};

function tools(kind: Kind, s: Settings): BetaTool[] {
  const list: BetaTool[] = [];
  if (kind !== "chat") {
    list.push({
      name: "send_message",
      description: "Надіслати користувачу повідомлення: воно з'явиться у кутку екрана його активного пристрою і в чаті.",
      input_schema: {
        type: "object",
        properties: {
          kind: {
            type: "string",
            enum: ["nudge", "ask", "plan", "review"],
            description: "nudge — м'яке нагадування, ask — питання, plan — план, review — підсумок дня",
          },
          text: { type: "string", description: "Текст повідомлення, 1–3 речення (план — до кількох пунктів)" },
        },
        required: ["kind", "text"],
        additionalProperties: false,
      },
    });
  }
  list.push({
    name: "add_task",
    description: "Додати задачу до списку задач користувача (з'явиться на всіх пристроях). Навичка — одна з наявних, за назвою.",
    input_schema: {
      type: "object",
      properties: {
        title: { type: "string", description: "Коротка назва задачі" },
        skill_name: { type: "string", description: "Назва наявної навички, до якої належить задача" },
        estimated_minutes: { type: "integer", description: "Оцінка тривалості в хвилинах" },
        difficulty: { type: "integer", description: "Складність 1–10 (необов'язково, за замовчуванням 3)" },
      },
      required: ["title", "skill_name", "estimated_minutes"],
      additionalProperties: false,
    },
  });
  if (s.mayStartFocus) {
    list.push({
      name: "start_focus",
      description: `Увімкнути фокус-режим на всіх пристроях: перелічені програми закриваються (з попередженням за хвилину) до кінця режиму. Максимум ${MAX_FOCUS_MINUTES} хв.`,
      input_schema: {
        type: "object",
        properties: {
          minutes: { type: "integer", description: "Тривалість у хвилинах" },
          apps: {
            type: "array",
            items: { type: "string" },
            description: "Ідентифікатори процесів для блокування (як у дужках у контексті)",
          },
          message: { type: "string", description: "Пояснення для користувача, 1–2 речення" },
        },
        required: ["minutes", "apps", "message"],
        additionalProperties: false,
      },
    });
  }
  list.push({
    name: "remember",
    description: "Запам'ятати коротке довгострокове спостереження про користувача на наступні дні.",
    input_schema: {
      type: "object",
      properties: { note: { type: "string", description: "Одне речення" } },
      required: ["note"],
      additionalProperties: false,
    },
  });
  return list;
}

// Opus, Sonnet і Fable підтримують резервну модель на сервері при відмові класифікатора безпеки
function supportsFallbacks(model: string): boolean {
  return model.startsWith("claude-opus") || model.startsWith("claude-sonnet") || model.startsWith("claude-fable");
}

async function run(kind: Kind, s: Settings, active: Presence | null) {
  const [state] = await sql`select memory from public.ft_assistant_state where id = 1`;
  const context = await describeNow(s, active);

  const system: BetaTextBlockParam[] = [
    { type: "text", text: systemPrompt(s.name || "Коуч") },
    { type: "text", text: profile(s, state?.memory ?? ""), cache_control: { type: "ephemeral" } },
  ];

  let messages: BetaMessageParam[];
  if (kind === "chat") {
    system.push({
      type: "text",
      text: "Зараз ти в чаті з користувачем. Відповідай коротко і по суті.\n\n" + context,
    });
    messages = await chatHistory();
    if (messages.length === 0) return;
  } else {
    messages = [{ role: "user", content: context + "\n\n## Завдання\n" + TASKS[kind] }];
  }

  const target = kind === "check" ? active?.device_id ?? null : null;
  const toolList = tools(kind, s);
  const texts: string[] = [];

  for (let round = 0; round < MAX_TOOL_ROUNDS; round++) {
    const response = await create({
      model: s.model || "claude-opus-5-5",
      max_tokens: 16000,
      system,
      tools: toolList,
      messages,
      output_config: { effort: kind === "check" ? "low" : "medium" },
    });

    if (response.stop_reason === "refusal") throw new Error("Claude відмовився відповідати на цей запит.");

    for (const block of response.content) {
      if (block.type === "text" && block.text.trim()) texts.push(block.text.trim());
    }
    if (response.stop_reason !== "tool_use") break;

    messages.push({ role: "assistant", content: response.content });
    const results: BetaToolResultBlockParam[] = [];
    for (const block of response.content) {
      if (block.type !== "tool_use") continue;
      try {
        const output = await executeTool(block.name, block.input as Record<string, unknown>, s, target);
        results.push({ type: "tool_result", tool_use_id: block.id, content: output });
      } catch (e) {
        results.push({ type: "tool_result", tool_use_id: block.id, content: errorText(e), is_error: true });
      }
    }
    messages.push({ role: "user", content: results });
  }

  if (kind === "chat") {
    const reply = texts.at(-1) ?? "Готово.";
    await sql`
      insert into public.ft_assistant_messages (role, kind, text, status)
      values ('assistant', 'chat', ${reply}, 'done')`;
  }
  // У перевірках і плані текст поза інструментами — міркування моделі, користувачу його не показуємо
  await sql`update public.ft_assistant_state set last_error = null where id = 1`;
}

async function create(params: MessageCreateParamsNonStreaming): Promise<BetaMessage> {
  if (supportsFallbacks(params.model)) {
    // "default": сервер сам обирає резервну модель за категорією відмови
    params = { ...params, betas: ["server-side-fallback-2026-07-01"], fallbacks: "default" };
  }
  return await anthropic.beta.messages.create(params);
}

// Останні ~30 повідомлень спільного чату; сусідні репліки одного боку зливаються,
// а розмова для Claude має починатися з користувача. Питання, що чекають відповіді,
// ставимо в кінець: між ними й відповіддю могло з'явитися нагадування з перевірки.
async function chatHistory(): Promise<BetaMessageParam[]> {
  const rows = await sql`
    select role, kind, text, status from (
      select id, role, kind, text, status from public.ft_assistant_messages
      order by id desc limit 30
    ) m order by status = 'pending', id`;

  const merged: { role: "user" | "assistant"; text: string }[] = [];
  for (const row of rows) {
    const role = row.role === "user" ? "user" : "assistant";
    const text = row.kind === "chat" ? row.text : `[${row.kind}] ${row.text}`;
    const last = merged.at(-1);
    if (last && last.role === role) last.text += "\n\n" + text;
    else merged.push({ role, text });
  }
  while (merged.length > 0 && merged[0].role !== "user") merged.shift();
  if (merged.at(-1)?.role !== "user") return [];
  return merged.map((m) => ({ role: m.role, content: m.text }));
}

// ===================== Інструменти =====================

async function executeTool(name: string, input: Record<string, unknown>, s: Settings, target: string | null): Promise<string> {
  switch (name) {
    case "send_message": {
      const kind = String(input.kind ?? "");
      const text = String(input.text ?? "").trim();
      if (!["nudge", "ask", "plan", "review"].includes(kind)) throw new Error(`Невідомий kind: ${kind}`);
      if (!text) throw new Error("Порожнє повідомлення");
      const [sent] = await sql`
        insert into public.ft_assistant_messages (role, kind, text, target_device_id, status)
        values ('assistant', ${kind}, ${text}, ${target}, 'done')
        returning id`;
      await pushMessage(sql, { id: Number(sent.id), kind, text, target });
      return "Надіслано.";
    }
    case "add_task":
      return await addTask(input);
    case "start_focus": {
      if (!s.mayStartFocus) throw new Error("Користувач не дозволив вмикати фокус-режим — лише пропонуй.");
      const minutes = Math.min(Math.max(Math.round(Number(input.minutes) || 0), 1), MAX_FOCUS_MINUTES);
      const apps = (Array.isArray(input.apps) ? input.apps : [])
        .map((a) => String(a).trim())
        .filter((a) => a && a.toLowerCase() !== FOCUS_TRACKER_APP);
      const message = String(input.message ?? "").trim();
      if (apps.length === 0) throw new Error("Не вказано жодної програми для блокування.");
      if (!message) throw new Error("Потрібне пояснення для користувача.");
      await sql`
        update public.ft_focus
        set until = now() + make_interval(mins => ${minutes}), reason = ${message},
            apps = ${sql.array(apps)}, updated_at = now()
        where id = 1`;
      const [sent] = await sql`
        insert into public.ft_assistant_messages (role, kind, text, target_device_id, status)
        values ('assistant', 'focus', ${message}, ${target}, 'done')
        returning id`;
      // Фокус-режим спільний, тож про нього знають усі телефони
      await pushMessage(sql, { id: Number(sent.id), kind: "focus", text: message, target: null });
      return `Фокус-режим увімкнено на ${minutes} хв; блокуються: ${apps.join(", ")}.`;
    }
    case "remember": {
      const note = String(input.note ?? "").trim();
      if (!note) throw new Error("Порожня нотатка");
      const [state] = await sql`select memory from public.ft_assistant_state where id = 1`;
      const date = zoned(new Date(), s.timeZone);
      let memory = `${(state?.memory ?? "").trim()}\n- ${date.date.slice(8, 10)}.${date.date.slice(5, 7)}: ${note}`.trim();
      // Найстаріші нотатки відкидаємо цілими рядками
      while (memory.length > MEMORY_MAX_CHARS && memory.includes("\n")) memory = memory.slice(memory.indexOf("\n") + 1);
      await sql`update public.ft_assistant_state set memory = ${memory} where id = 1`;
      return "Запам'ятав.";
    }
    default:
      throw new Error(`Невідомий інструмент ${name}`);
  }
}

// Задача у форматі TaskDoc застосунку (CloudSyncService.Entities.cs, JSON camelCase)
async function addTask(input: Record<string, unknown>): Promise<string> {
  const title = String(input.title ?? "").trim();
  const skillName = String(input.skill_name ?? "").trim();
  if (!title) throw new Error("Порожня назва задачі");

  const skills = await sql`
    select sync_id::text as id, data->>'name' as name from public.ft_entities
    where kind = 'skill' and not deleted and data is not null`;
  if (skills.length === 0) {
    throw new Error("У користувача ще немає жодної навички, а задача мусить належати навичці. Запропонуй йому створити навичку на сторінці «Навички».");
  }
  const lower = skillName.toLowerCase();
  const skill = skills.find((k) => String(k.name).toLowerCase() === lower)
    ?? skills.find((k) => String(k.name).toLowerCase().includes(lower) && lower.length > 0);
  if (!skill) {
    throw new Error(`Навички «${skillName}» немає. Наявні: ${skills.map((k) => k.name).join(", ")}`);
  }

  const settings = await loadSettings();
  const doc = {
    title,
    description: "",
    skillId: skill.id,
    estimatedMinutes: Math.min(Math.max(Math.round(Number(input.estimated_minutes) || 30), 1), 600),
    difficulty: Math.min(Math.max(Math.round(Number(input.difficulty) || 3), 1), 10),
    // Застосунок зберігає дату створення за місцевим часом, без зсуву
    dateCreated: zoned(new Date(), settings?.timeZone ?? "UTC").iso,
    completed: false,
    earnedXp: null,
    activeSeconds: null,
    isPassive: false,
    programs: [] as string[],
  };
  await sql`
    insert into public.ft_entities (kind, sync_id, data, deleted, updated_at, updated_by, synced_at)
    values ('task', gen_random_uuid(), ${sql.json(doc)}, false, now(), ${CLOUD_ASSISTANT_ID}, now())`;
  return `Задачу «${title}» додано до навички «${skill.name}».`;
}

// ===================== Контекст =====================

async function loadSettings(): Promise<Settings | null> {
  const [row] = await sql`select data from public.ft_assistant_settings where id = 1`;
  if (!row) return null;
  const d = row.data as Partial<Settings>;
  return {
    goals: d.goals ?? "",
    rules: d.rules ?? "",
    fromHour: Number(d.fromHour ?? 12),
    toHour: Number(d.toHour ?? 18),
    checkMinutes: Number(d.checkMinutes ?? 20),
    mayStartFocus: d.mayStartFocus ?? false,
    model: d.model || "claude-opus-5-5",
    enabled: d.enabled ?? false,
    timeZone: validTimeZone(d.timeZone),
    name: d.name || "Коуч",
  };
}

// Пристрій, за яким користувач щось вводив за останні ~10 хвилин (найсвіжіший)
async function activeDevice(): Promise<Presence | null> {
  const [row] = await sql`
    select device_id::text as device_id, name, last_input_at, foreground_app from public.ft_presence
    where last_input_at > now() - make_interval(mins => ${ACTIVE_WINDOW_MINUTES})
    order by last_input_at desc limit 1`;
  return (row as Presence | undefined) ?? null;
}

async function describeNow(s: Settings, active: Presence | null): Promise<string> {
  const tz = s.timeZone;
  const now = new Date();
  const local = zoned(now, tz);
  const out: string[] = [];

  out.push(`## Зараз: ${longDate(now, tz)} (${tz})`);

  const presence = await sql`
    select device_id::text as device_id, name, last_input_at, foreground_app from public.ft_presence order by name`;
  out.push(active
    ? `Активний пристрій: ${active.name} (у фокусі: ${active.foreground_app ?? "?"}).`
    : `Зараз користувач не за комп'ютером (введення не було понад ${ACTIVE_WINDOW_MINUTES} хв).`);
  for (const p of presence as unknown as Presence[]) {
    out.push(`- ${p.name}: останнє введення ${p.last_input_at ? zoned(new Date(p.last_input_at), tz).hhmm + " " + dayLabel(new Date(p.last_input_at), tz, local.date) : "невідомо"}, у фокусі ${p.foreground_app ?? "?"}`);
  }

  const [focus] = await sql`select until, reason, apps from public.ft_focus where id = 1 and until > now()`;
  out.push(focus
    ? `Фокус-режим увімкнено до ${zoned(new Date(focus.until), tz).hhmm} (${focus.reason}); заблоковано: ${(focus.apps as string[]).join(", ")}`
    : "Фокус-режим вимкнено.");

  out.push("", "## Останні 60 хвилин (пристрій: програма — заголовок вікна)");
  out.push(await describeRecent(tz, presence as unknown as Presence[]));

  out.push("## Сьогодні на всіх пристроях");
  out.push(await describeToday(local.date));

  out.push("## Останні 7 днів (по днях: категорії й топ-3 програми)");
  out.push(await describeWeek(local.date));

  out.push("## Задачі");
  out.push(await describeTasks(tz, local.date));

  out.push("## Повідомлення сьогодні (твої й користувача)");
  const today = await sql`
    select created_at, role, kind, text from public.ft_assistant_messages
    where (created_at at time zone ${tz})::date = ${local.date}::date
    order by id desc limit 12`;
  out.push(today.length === 0
    ? "(ще не було)"
    : today.reverse().map((m) =>
      `${zoned(new Date(m.created_at), tz).hhmm} [${m.role === "user" ? "користувач" : m.kind}] ${m.text}`).join("\n"));

  return out.join("\n");
}

async function describeRecent(tz: string, presence: Presence[]): Promise<string> {
  const rows = await sql`
    select device_id::text as device_id, at, seconds, app, title from public.ft_recent_activity
    where at + make_interval(secs => seconds) >= now() - interval '60 minutes'
    order by at`;
  if (rows.length === 0) return "(немає даних: користувача не було за жодним пристроєм)\n";

  const names = new Map(presence.map((p) => [p.device_id, p.name]));
  const lines: string[] = [];
  let shortCount = 0;
  let shortSeconds = 0;
  for (const r of rows) {
    const seconds = Number(r.seconds);
    if (seconds < 30) {
      shortCount++;
      shortSeconds += seconds;
      continue;
    }
    const start = new Date(r.at);
    const end = new Date(start.getTime() + seconds * 1000);
    lines.push(`${zoned(start, tz).hhmm}–${zoned(end, tz).hhmm} (${duration(seconds)}) ${names.get(r.device_id) ?? "?"}: ${r.app} — ${r.title}`);
  }
  if (shortCount > 0) lines.push(`…і ще ${shortCount} коротких перемикань (разом ${duration(shortSeconds)})`);
  return lines.join("\n") + "\n";
}

async function describeToday(today: string): Promise<string> {
  const apps = await sql`
    select h.app_name, coalesce(p.category, 'Суміжні') as category,
           sum(h.total_seconds) as total, sum(h.active_seconds) as active
    from public.ft_hourly_usage h left join public.ft_programs p on p.identifier = h.app_name
    where h.date = ${today}::date
    group by h.app_name, p.category
    order by total desc`;

  // Хвилини присутності: біт m — байт m/8, біт m%8 (молодший перший); хвилина рахується
  // один раз, навіть якщо користувач був за кількома пристроями одночасно
  const bitmaps = await sql`select minutes from public.ft_activity where date = ${today}::date`;
  const merged = new Uint8Array(180);
  for (const b of bitmaps) {
    const bytes = b.minutes as Uint8Array;
    for (let i = 0; i < merged.length && i < bytes.length; i++) merged[i] |= bytes[i];
  }
  let minutes = 0;
  for (const byte of merged) for (let bit = 0; bit < 8; bit++) if (byte & (1 << bit)) minutes++;

  const lines = [`Час за комп'ютером: ${duration(minutes * 60)}`];
  const byCategory = new Map<string, number>();
  for (const a of apps) byCategory.set(a.category, (byCategory.get(a.category) ?? 0) + Number(a.total));
  for (const [category, total] of byCategory) lines.push(`${category}: ${duration(total)}`);
  lines.push("Топ програм:");
  if (apps.length === 0) lines.push("(поки нічого)");
  for (const a of apps.slice(0, 12)) {
    lines.push(`- ${a.app_name} [${a.category}]: ${duration(Number(a.total))}, активно ${duration(Number(a.active))}`);
  }
  return lines.join("\n") + "\n";
}

async function describeWeek(today: string): Promise<string> {
  const rows = await sql`
    select h.date::text as date, h.app_name, coalesce(p.category, 'Суміжні') as category, sum(h.total_seconds) as total
    from public.ft_hourly_usage h left join public.ft_programs p on p.identifier = h.app_name
    where h.date >= ${today}::date - 7 and h.date < ${today}::date
    group by h.date, h.app_name, p.category`;
  if (rows.length === 0) return "(немає даних)\n";

  const days = new Map<string, { app: string; category: string; total: number }[]>();
  for (const r of rows) {
    const list = days.get(r.date) ?? [];
    list.push({ app: r.app_name, category: r.category, total: Number(r.total) });
    days.set(r.date, list);
  }
  const lines: string[] = [];
  for (const date of [...days.keys()].sort()) {
    const list = days.get(date)!;
    const categories = new Map<string, number>();
    for (const a of list) categories.set(a.category, (categories.get(a.category) ?? 0) + a.total);
    const top = list.sort((a, b) => b.total - a.total).slice(0, 3).map((a) => `${a.app} ${duration(a.total)}`);
    const weekday = new Date(`${date}T12:00:00Z`).toLocaleDateString("uk-UA", { weekday: "short", timeZone: "UTC" });
    lines.push(`${date.slice(8, 10)}.${date.slice(5, 7)} ${weekday}: ${[...categories].map(([c, t]) => `${c} ${duration(t)}`).join(", ")}; топ: ${top.join(", ")}`);
  }
  return lines.join("\n") + "\n";
}

async function describeTasks(tz: string, today: string): Promise<string> {
  const skills = new Map<string, string>();
  for (const k of await sql`select sync_id::text as id, data->>'name' as name from public.ft_entities where kind = 'skill' and not deleted`) {
    skills.set(k.id, k.name);
  }
  const tasks = await sql`
    select data, updated_at from public.ft_entities
    where kind = 'task' and not deleted and data is not null`;

  const open = tasks.filter((t) => !t.data.completed);
  const doneToday = tasks.filter((t) =>
    t.data.completed && zoned(new Date(t.updated_at), tz).date === today).length;

  const lines = [`Навички: ${[...skills.values()].join(", ") || "(немає)"}`];
  lines.push(`Виконано сьогодні: ${doneToday}. Відкриті:`);
  if (open.length === 0) lines.push("(немає відкритих задач)");
  for (const t of open.slice(0, 15)) {
    const d = t.data;
    const created = typeof d.dateCreated === "string" ? `${d.dateCreated.slice(8, 10)}.${d.dateCreated.slice(5, 7)}` : "?";
    lines.push(`- ${d.title} [${skills.get(d.skillId) ?? "?"}], ~${d.estimatedMinutes} хв, створено ${created}`);
  }
  return lines.join("\n") + "\n";
}

// ===================== Час =====================

function validTimeZone(tz: unknown): string {
  if (typeof tz === "string" && tz) {
    try {
      new Intl.DateTimeFormat("en-US", { timeZone: tz });
      return tz;
    } catch {
      // невідома зона — UTC
    }
  }
  return "UTC";
}

function zoned(date: Date, tz: string) {
  const parts = Object.fromEntries(
    new Intl.DateTimeFormat("en-CA", {
      timeZone: tz,
      year: "numeric",
      month: "2-digit",
      day: "2-digit",
      hour: "2-digit",
      minute: "2-digit",
      second: "2-digit",
      hourCycle: "h23",
    }).formatToParts(date).map((p) => [p.type, p.value]),
  );
  const d = `${parts.year}-${parts.month}-${parts.day}`;
  return {
    date: d,
    hour: Number(parts.hour),
    hhmm: `${parts.hour}:${parts.minute}`,
    iso: `${d}T${parts.hour}:${parts.minute}:${parts.second}`,
  };
}

function longDate(date: Date, tz: string): string {
  return new Intl.DateTimeFormat("uk-UA", {
    timeZone: tz,
    weekday: "long",
    day: "2-digit",
    month: "2-digit",
    year: "numeric",
    hour: "2-digit",
    minute: "2-digit",
    hourCycle: "h23",
  }).format(date);
}

function dayLabel(date: Date, tz: string, today: string): string {
  const d = zoned(date, tz).date;
  return d === today ? "сьогодні" : `${d.slice(8, 10)}.${d.slice(5, 7)}`;
}

function duration(seconds: number): string {
  if (seconds < 60) return `${Math.round(seconds)} с`;
  const minutes = Math.floor(seconds / 60);
  return minutes >= 60 ? `${Math.floor(minutes / 60)} год ${minutes % 60} хв` : `${minutes} хв`;
}

function pad(n: number): string {
  return String(n).padStart(2, "0");
}
