// Навички, обмеження й задачі, які Коуч створює або видаляє на прохання користувача.
// Записуються в ft_entities у форматі документів застосунку (CloudSyncService.Entities.cs, JSON camelCase)
// від імені асистента: пристрої завантажують їх із синхронізацією, як зміни з іншого пристрою.

import type postgres from "npm:postgres@3.4.9";
import type { BetaTool } from "npm:@anthropic-ai/sdk@0.128.0/resources/beta/messages/messages";

type Sql = ReturnType<typeof postgres>;

export const CLOUD_ASSISTANT_ID = "00000000-0000-4000-8000-0000a5515700";
const DEFAULT_CATEGORY_COLOR = "#4A90D9";

interface Entity {
  id: string;
  data: Record<string, unknown>;
}

export const entityTools: BetaTool[] = [
  {
    name: "add_skill",
    description: "Створити навичку (з'явиться на сторінці «Навички» на всіх пристроях). Категорію буде створено, якщо її ще немає.",
    input_schema: {
      type: "object",
      properties: {
        name: { type: "string", description: "Назва навички, напр. «Kubernetes»" },
        category: { type: "string", description: "Назва категорії навичок, напр. «DevOps»" },
      },
      required: ["name", "category"],
      additionalProperties: false,
    },
  },
  {
    name: "add_restriction",
    description: "Створити обмеження для програм на всіх пристроях: денний ліміт часу (за сумою пристроїв) і/або заборону в проміжок часу. Програми — як у статистиці: процеси ПК (dota2, steam) і назви програм телефону (YouTube, Instagram).",
    input_schema: {
      type: "object",
      properties: {
        note: { type: "string", description: "Пояснення, яке користувач побачить при спрацюванні" },
        programs: { type: "array", items: { type: "string" }, description: "Програми, до яких діє обмеження" },
        daily_limit_minutes: { type: "integer", description: "Скільки хвилин на день можна (необов'язково)" },
        blocked_from: { type: "string", description: "Початок заборони, ГГ:ХХ (необов'язково, разом із blocked_to)" },
        blocked_to: { type: "string", description: "Кінець заборони, ГГ:ХХ" },
      },
      required: ["note", "programs"],
      additionalProperties: false,
    },
  },
  {
    name: "delete_task",
    description: "Видалити задачу користувача за назвою (лише на його прохання).",
    input_schema: {
      type: "object",
      properties: { title: { type: "string", description: "Назва задачі або її частина" } },
      required: ["title"],
      additionalProperties: false,
    },
  },
  {
    name: "delete_restriction",
    description: "Видалити обмеження за його поясненням (лише на прохання користувача).",
    input_schema: {
      type: "object",
      properties: { note: { type: "string", description: "Пояснення обмеження або його частина" } },
      required: ["note"],
      additionalProperties: false,
    },
  },
];

export async function executeEntityTool(sql: Sql, name: string, input: Record<string, unknown>): Promise<string | null> {
  switch (name) {
    case "add_skill":
      return await addSkill(sql, String(input.name ?? "").trim(), String(input.category ?? "").trim());
    case "add_restriction":
      return await addRestriction(sql, input);
    case "delete_task":
      return await deleteByText(sql, "task", "title", String(input.title ?? ""), "Задачу");
    case "delete_restriction":
      return await deleteByText(sql, "restriction", "note", String(input.note ?? ""), "Обмеження");
    default:
      return null;
  }
}

async function addSkill(sql: Sql, name: string, category: string): Promise<string> {
  if (!name || !category) throw new Error("Потрібні назва навички й категорія");

  const skills = await live(sql, "skill");
  if (skills.some((s) => lower(s.data.name) === lower(name))) return `Навичка «${name}» вже є.`;

  const categories = await live(sql, "category");
  let categoryId = categories.find((c) => lower(c.data.name) === lower(category))?.id;
  if (!categoryId) {
    categoryId = await insert(sql, "category", { name: category, color: DEFAULT_CATEGORY_COLOR, sortOrder: categories.length });
  }
  await insert(sql, "skill", { name, categoryId, level: 1, xp: 0 });
  return `Навичку «${name}» створено в категорії «${category}».`;
}

// Правила — ті самі, що на сторінці «Обмеження»: max_total_seconds (значення — хвилини) і time_interval («ГГ:ХХ–ГГ:ХХ»)
async function addRestriction(sql: Sql, input: Record<string, unknown>): Promise<string> {
  const note = String(input.note ?? "").trim();
  const programs = (Array.isArray(input.programs) ? input.programs : []).map((p) => String(p).trim()).filter((p) => p);
  if (!note || programs.length === 0) throw new Error("Потрібні пояснення й хоча б одна програма");
  if (programs.some((p) => p.toLowerCase() === "focustracker.app")) throw new Error("FocusTracker обмежувати не можна");

  const rules: { ruleType: string; value: string }[] = [];
  const limit = Math.round(Number(input.daily_limit_minutes) || 0);
  if (limit > 0) rules.push({ ruleType: "max_total_seconds", value: String(limit) });
  const from = time(input.blocked_from), to = time(input.blocked_to);
  if (from && to) rules.push({ ruleType: "time_interval", value: `${from}–${to}` });
  if (rules.length === 0) throw new Error("Вкажи денний ліміт (daily_limit_minutes) і/або проміжок заборони (blocked_from, blocked_to)");

  await insert(sql, "restriction", { note, rules, programs });
  const parts = [limit > 0 ? `${limit} хв на день` : "", from && to ? `заборона ${from}–${to}` : ""].filter((p) => p);
  return `Обмеження «${note}» для ${programs.join(", ")} створено: ${parts.join(", ")}.`;
}

async function deleteByText(sql: Sql, kind: string, field: string, text: string, what: string): Promise<string> {
  const query = lower(text);
  if (!query) throw new Error("Порожня назва");
  const items = await live(sql, kind);
  const matches = items.filter((i) => lower(i.data[field]) === query);
  const found = matches.length > 0 ? matches : items.filter((i) => lower(i.data[field]).includes(query));
  if (found.length === 0) throw new Error(`${what} «${text}» не знайдено`);
  if (found.length > 1) throw new Error(`Під «${text}» підходять кілька: ${found.map((i) => i.data[field]).join("; ")}. Уточни.`);

  await sql`
    update public.ft_entities
    set data = null, deleted = true, updated_at = now(), updated_by = ${CLOUD_ASSISTANT_ID}, synced_at = now()
    where kind = ${kind} and sync_id = ${found[0].id}`;
  return `${what} «${found[0].data[field]}» видалено.`;
}

// Список обмежень для контексту Коуча
export async function describeRestrictions(sql: Sql): Promise<string> {
  const items = await live(sql, "restriction");
  if (items.length === 0) return "(немає)\n";
  return items.map((r) => {
    const rules = (Array.isArray(r.data.rules) ? r.data.rules : []) as { ruleType: string; value: string }[];
    const text = rules.map((rule) =>
      rule.ruleType === "max_total_seconds" ? `${rule.value} хв на день`
        : rule.ruleType === "time_interval" ? `заборона ${rule.value}`
        : `${rule.ruleType} ${rule.value}`).join(", ");
    const programs = Array.isArray(r.data.programs) ? (r.data.programs as string[]).join(", ") : "";
    return `- ${r.data.note}: ${programs} (${text})`;
  }).join("\n") + "\n";
}

async function live(sql: Sql, kind: string): Promise<Entity[]> {
  const rows = await sql`
    select sync_id::text as id, data from public.ft_entities
    where kind = ${kind} and not deleted and data is not null`;
  return rows.map((r) => ({ id: r.id as string, data: r.data as Record<string, unknown> }));
}

async function insert(sql: Sql, kind: string, doc: Record<string, unknown>): Promise<string> {
  const [row] = await sql`
    insert into public.ft_entities (kind, sync_id, data, deleted, updated_at, updated_by, synced_at)
    values (${kind}, gen_random_uuid(), ${sql.json(doc as never)}, false, now(), ${CLOUD_ASSISTANT_ID}, now())
    returning sync_id::text as id`;
  return row.id as string;
}

function lower(value: unknown): string {
  return String(value ?? "").trim().toLowerCase();
}

function time(value: unknown): string | null {
  const match = /^(\d{1,2}):(\d{2})$/.exec(String(value ?? "").trim());
  if (!match) return null;
  const h = Number(match[1]), m = Number(match[2]);
  return h < 24 && m < 60 ? `${String(h).padStart(2, "0")}:${match[2]}` : null;
}
