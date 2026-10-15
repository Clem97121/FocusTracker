// Пам'ять Коуча про користувача — три рівні в базі:
//   ft_coach_profile — профіль «хто ти» за розділами; завжди в контексті, Коуч дописує й переписує його;
//   ft_coach_journal — щоденник: короткий підсумок кожного дня; останній тиждень у контексті, старіше — пошуком;
//   щотижнева переробка — Коуч перечитує тиждень щоденника і оновлює профіль (узагальнює, прибирає застаріле).
// Таблиці створює сама функція (вона працює від імені власника бази); пристрої можуть їх читати.

import type postgres from "npm:postgres@3.4.9";
import type { BetaTool } from "npm:@anthropic-ai/sdk@0.128.0/resources/beta/messages/messages";

type Sql = ReturnType<typeof postgres>;

const SECTION_MAX_CHARS = 1500;
const JOURNAL_DAYS_IN_CONTEXT = 7;

export const SECTIONS: Record<string, string> = {
  about: "Хто він: робота, навчання, життєві обставини, люди поруч",
  goals: "Що для нього важливо і чому (глибше, ніж список цілей)",
  patterns: "Звички й закономірності: коли продуктивний, як і на що прокрастинує",
  works: "Що допомагає: які підходи, формулювання, нагадування спрацьовують",
  fails: "Що не працює або дратує",
  agreements: "Домовленості й обіцянки (з датами)",
  notes: "Інше",
};

let schemaReady = false;

export async function ensureMemorySchema(sql: Sql) {
  if (schemaReady) return;
  await sql.unsafe(`
    create table if not exists public.ft_coach_profile (
      section text primary key,
      content text not null default '',
      updated_at timestamptz not null default now()
    );
    create table if not exists public.ft_coach_journal (
      date date primary key,
      summary text not null,
      updated_at timestamptz not null default now()
    );
    alter table public.ft_coach_profile enable row level security;
    alter table public.ft_coach_journal enable row level security;
    alter table public.ft_assistant_state add column if not exists consolidated_date date;
    alter table public.ft_assistant_messages drop constraint if exists ft_assistant_messages_kind_check;
    alter table public.ft_assistant_messages add constraint ft_assistant_messages_kind_check
      check (kind in ('chat', 'nudge', 'ask', 'plan', 'focus', 'review', 'event'));
    do $$ begin
      if to_regprocedure('public.ft_is_owner()') is not null then
        drop policy if exists ft_owner_read on public.ft_coach_profile;
        create policy ft_owner_read on public.ft_coach_profile for select to authenticated using (public.ft_is_owner());
        drop policy if exists ft_owner_read on public.ft_coach_journal;
        create policy ft_owner_read on public.ft_coach_journal for select to authenticated using (public.ft_is_owner());
      end if;
    end $$;`);

  // Старі нотатки remember (один текст у ft_assistant_state.memory) переносимо в розділ «Інше»
  const [profileRows] = await sql`select count(*)::int as n from public.ft_coach_profile`;
  if (profileRows.n === 0) {
    const [state] = await sql`select memory from public.ft_assistant_state where id = 1`;
    const old = String(state?.memory ?? "").trim();
    if (old) await sql`insert into public.ft_coach_profile (section, content) values ('notes', ${old}) on conflict do nothing`;
  }
  schemaReady = true;
}

// Профіль і свіжий щоденник — у системний промпт
export async function describeMemory(sql: Sql, today: string): Promise<string> {
  const rows = await sql`select section, content from public.ft_coach_profile where content <> ''`;
  const bySection = new Map(rows.map((r) => [r.section as string, r.content as string]));
  const profile = Object.entries(SECTIONS)
    .filter(([key]) => bySection.has(key))
    .map(([key, title]) => `### ${title} [${key}]\n${bySection.get(key)}`)
    .join("\n\n");

  const journal = await sql`
    select date::text as date, summary from public.ft_coach_journal
    where date < ${today}::date order by date desc limit ${JOURNAL_DAYS_IN_CONTEXT}`;
  const days = journal.reverse().map((j) => `- ${j.date}: ${j.summary}`).join("\n");

  const empty = rows.length === 0;
  return `## Що ти знаєш про користувача (профіль)
${profile || "(поки майже нічого)"}
${empty ? "\nПрофіль порожній: у розмовах природно, по одному питанню, розпитуй про роботу, цілі, звички — і записуй через remember.\n" : ""}
## Щоденник останніх днів
${days || "(записів ще немає)"}
Старіші дні можна знайти через search_journal.`;
}

export const memoryTools: BetaTool[] = [
  {
    name: "remember",
    description: "Дописати до профілю користувача важливий довгостроковий факт чи спостереження (не дрібниці дня — для них щоденник).",
    input_schema: {
      type: "object",
      properties: {
        section: { type: "string", enum: Object.keys(SECTIONS), description: sectionsHelp() },
        note: { type: "string", description: "Одне коротке речення" },
      },
      required: ["section", "note"],
      additionalProperties: false,
    },
  },
  {
    name: "rewrite_profile_section",
    description: "Повністю переписати розділ профілю: виправити застаріле, прибрати те, що користувач попросив забути, стиснути повтори. Порожній текст — очистити розділ.",
    input_schema: {
      type: "object",
      properties: {
        section: { type: "string", enum: Object.keys(SECTIONS), description: sectionsHelp() },
        content: { type: "string", description: "Новий текст розділу: короткі пункти «- …»" },
      },
      required: ["section", "content"],
      additionalProperties: false,
    },
  },
  {
    name: "search_journal",
    description: "Знайти в щоденнику минулі дні за словом (напр. «співбесіда», «Dota») або за періодом.",
    input_schema: {
      type: "object",
      properties: {
        query: { type: "string", description: "Слово чи фраза (необов'язково)" },
        from: { type: "string", description: "Дата від, YYYY-MM-DD (необов'язково)" },
        to: { type: "string", description: "Дата до, YYYY-MM-DD (необов'язково)" },
      },
      additionalProperties: false,
    },
  },
];

export const journalTool: BetaTool = {
  name: "write_journal",
  description: "Записати підсумок сьогоднішнього дня в щоденник (перезаписує запис за сьогодні).",
  input_schema: {
    type: "object",
    properties: {
      summary: {
        type: "string",
        description: "2–5 речень: що робив і скільки, що вийшло для цілей, що завадило, настрій чи важливі події з чату",
      },
    },
    required: ["summary"],
    additionalProperties: false,
  },
};

function sectionsHelp(): string {
  return Object.entries(SECTIONS).map(([k, v]) => `${k} — ${v}`).join("; ");
}

function section(input: Record<string, unknown>): string {
  const key = String(input.section ?? "");
  if (!(key in SECTIONS)) throw new Error(`Невідомий розділ: ${key}`);
  return key;
}

export async function executeMemoryTool(
  sql: Sql, name: string, input: Record<string, unknown>, today: string,
): Promise<string | null> {
  switch (name) {
    case "remember": {
      const key = section(input);
      const note = String(input.note ?? "").trim();
      if (!note) throw new Error("Порожня нотатка");
      const [row] = await sql`select content from public.ft_coach_profile where section = ${key}`;
      let content = `${String(row?.content ?? "").trim()}\n- ${note} (${today.slice(8, 10)}.${today.slice(5, 7)})`.trim();
      // Переповнений розділ втрачає найстаріші пункти; щотижнева переробка стискає їх раніше
      while (content.length > SECTION_MAX_CHARS && content.includes("\n")) content = content.slice(content.indexOf("\n") + 1);
      await upsertSection(sql, key, content);
      return "Запам'ятав.";
    }
    case "rewrite_profile_section": {
      const key = section(input);
      await upsertSection(sql, key, String(input.content ?? "").trim().slice(0, SECTION_MAX_CHARS));
      return "Розділ оновлено.";
    }
    case "search_journal": {
      const query = String(input.query ?? "").trim();
      const from = /^\d{4}-\d{2}-\d{2}$/.test(String(input.from)) ? String(input.from) : "1900-01-01";
      const to = /^\d{4}-\d{2}-\d{2}$/.test(String(input.to)) ? String(input.to) : "2999-12-31";
      const rows = await sql`
        select date::text as date, summary from public.ft_coach_journal
        where date between ${from}::date and ${to}::date
          and (${query}::text = '' or summary ilike ${"%" + query + "%"})
        order by date desc limit 14`;
      return rows.length === 0 ? "Нічого не знайдено." : rows.map((r) => `${r.date}: ${r.summary}`).join("\n");
    }
    case "write_journal": {
      const summary = String(input.summary ?? "").trim();
      if (!summary) throw new Error("Порожній запис");
      await sql`
        insert into public.ft_coach_journal (date, summary) values (${today}::date, ${summary})
        on conflict (date) do update set summary = excluded.summary, updated_at = now()`;
      return "Записано в щоденник.";
    }
    default:
      return null;
  }
}

async function upsertSection(sql: Sql, key: string, content: string) {
  await sql`
    insert into public.ft_coach_profile (section, content) values (${key}, ${content})
    on conflict (section) do update set content = excluded.content, updated_at = now()`;
}

export const CONSOLIDATE_TASK =
  "Щотижнева переробка пам'яті. Перечитай профіль і щоденник останніх днів (за потреби — search_journal). " +
  "Онови профіль через rewrite_profile_section: узагальни повторювані закономірності тижня, " +
  "прибери застаріле й дублікати, тримай кожен розділ коротким (до ~10 пунктів). Користувачу нічого не надсилай.";
