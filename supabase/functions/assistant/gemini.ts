// Gemini як альтернатива Claude: той самий цикл із інструментами в index.ts працює з форматом Claude,
// а тут запит перекладається в generateContent, а відповідь — назад.
// Секрет функції: GEMINI_API_KEY (ключ з Google AI Studio).

import type {
  BetaMessage,
  BetaMessageParam,
  MessageCreateParamsNonStreaming,
} from "npm:@anthropic-ai/sdk@0.128.0/resources/beta/messages/messages";

const API_ROOT = "https://generativelanguage.googleapis.com/v1beta/models";

// Частина відповіді Gemini зі службовим підписом міркувань; повертати її треба без змін
// deno-lint-ignore no-explicit-any
type Part = Record<string, any>;
// Блок відповіді у форматі Claude разом із вихідною частиною Gemini
type Block = Record<string, unknown> & { _part?: Part };

export function isGemini(model: string): boolean {
  return model.startsWith("gemini");
}

export async function createGemini(params: MessageCreateParamsNonStreaming): Promise<BetaMessage> {
  const key = Deno.env.get("GEMINI_API_KEY");
  if (!key) throw new Error("Не задано секрет GEMINI_API_KEY у Supabase, тому Gemini не може відповісти.");

  const response = await fetch(`${API_ROOT}/${encodeURIComponent(params.model)}:generateContent`, {
    method: "POST",
    headers: { "content-type": "application/json", "x-goog-api-key": key },
    body: JSON.stringify(toRequest(params)),
  });
  if (!response.ok) {
    const body = await response.text();
    if (response.status === 429) throw new Error(`Вичерпано безкоштовний ліміт Gemini, спробуйте пізніше. ${body.slice(0, 300)}`);
    throw new Error(`Gemini API ${response.status}: ${body.slice(0, 500)}`);
  }
  return fromResponse(await response.json(), params.model);
}

export function toRequest(params: MessageCreateParamsNonStreaming) {
  const system = typeof params.system === "string"
    ? params.system
    : (params.system ?? []).map((b) => b.text).join("\n\n");
  const effort = (params as { output_config?: { effort?: string } }).output_config?.effort;

  return {
    systemInstruction: { parts: [{ text: system }] },
    contents: toContents(params.messages),
    tools: [{
      functionDeclarations: (params.tools ?? []).map((tool) => {
        const t = tool as { name: string; description?: string; input_schema: unknown };
        return { name: t.name, description: t.description, parametersJsonSchema: t.input_schema };
      }),
    }],
    generationConfig: {
      maxOutputTokens: params.max_tokens,
      thinkingConfig: { thinkingLevel: effort === "low" ? "LOW" : "MEDIUM" },
    },
  };
}

function toContents(messages: BetaMessageParam[]) {
  // tool_result у форматі Claude знає лише id виклику, а Gemini потрібна ще й назва функції
  const calls = new Map<string, Part>();

  return messages.map((message) => {
    const role = message.role === "assistant" ? "model" : "user";
    if (typeof message.content === "string") return { role, parts: [{ text: message.content }] };

    const parts: Part[] = [];
    for (const raw of message.content as unknown as Block[]) {
      if (raw.type === "tool_use") calls.set(raw.id as string, raw._part!.functionCall);
      if (raw._part) {
        parts.push(raw._part);
      } else if (raw.type === "text") {
        parts.push({ text: raw.text });
      } else if (raw.type === "tool_result") {
        const call = calls.get(raw.tool_use_id as string);
        if (!call) throw new Error(`Результат для невідомого виклику ${raw.tool_use_id}`);
        parts.push({
          functionResponse: {
            ...(call.id ? { id: call.id } : {}),
            name: call.name,
            response: raw.is_error ? { error: raw.content } : { result: raw.content },
          },
        });
      }
    }
    return { role, parts };
  });
}

// deno-lint-ignore no-explicit-any
export function fromResponse(body: any, model: string): BetaMessage {
  const candidate = body.candidates?.[0];
  const parts: Part[] = candidate?.content?.parts ?? [];
  const content: Block[] = [];

  for (const part of parts) {
    // Міркування моделі не показуємо і не запитуємо; підпис живе на звичайних частинах
    if (part.thought) continue;
    if (part.functionCall) {
      content.push({
        type: "tool_use",
        id: part.functionCall.id ?? crypto.randomUUID(),
        name: part.functionCall.name,
        input: part.functionCall.args ?? {},
        _part: part,
      });
    } else if (typeof part.text === "string") {
      content.push({ type: "text", text: part.text, _part: part });
    }
  }

  return {
    id: body.responseId ?? "",
    type: "message",
    role: "assistant",
    model,
    content,
    stop_reason: stopReason(body, candidate?.finishReason, content),
  } as unknown as BetaMessage;
}

function stopReason(body: { promptFeedback?: { blockReason?: string } }, finish: string | undefined, content: Block[]) {
  if (body.promptFeedback?.blockReason) return "refusal";
  if (content.some((b) => b.type === "tool_use")) return "tool_use";
  switch (finish) {
    case "MAX_TOKENS":
      return "max_tokens";
    case "SAFETY":
    case "RECITATION":
    case "BLOCKLIST":
    case "PROHIBITED_CONTENT":
    case "SPII":
      return "refusal";
    case "STOP":
    case undefined:
      return "end_turn";
    default:
      throw new Error(`Gemini завершив відповідь із причиною ${finish}`);
  }
}
