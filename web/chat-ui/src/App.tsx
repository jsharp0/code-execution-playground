import { useMemo, useState } from "react";

type ChatMessage = {
  id: string;
  role: "user" | "assistant";
  content: string;
};

type ChatResponse = {
  reply?: string;
  message?: string;
  response?: string;
};

const apiBase = import.meta.env.VITE_MCP_BASE_URL ?? "";

const initialMessage: ChatMessage = {
  id: "welcome",
  role: "assistant",
  content: "Hi! Ask me anything and I will reply via the MCP client host.",
};

export default function App() {
  const [messages, setMessages] = useState<ChatMessage[]>([initialMessage]);
  const [input, setInput] = useState("");
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canSend = useMemo(() => input.trim().length > 0 && !isLoading, [input, isLoading]);

  const handleSend = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (!canSend) {
      return;
    }

    setError(null);
    const trimmed = input.trim();
    const userMessage: ChatMessage = {
      id: `${Date.now()}-user`,
      role: "user",
      content: trimmed,
    };

    setMessages((prev) => [...prev, userMessage]);
    setInput("");
    setIsLoading(true);

    try {
      const response = await fetch(`${apiBase}/chat`, {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
        },
        body: JSON.stringify({ message: trimmed }),
      });

      if (!response.ok) {
        throw new Error(`Request failed (${response.status})`);
      }

      const data: ChatResponse = await response.json();
      const reply = data.reply ?? data.message ?? data.response ?? "No reply returned.";
      const assistantMessage: ChatMessage = {
        id: `${Date.now()}-assistant`,
        role: "assistant",
        content: reply,
      };

      setMessages((prev) => [...prev, assistantMessage]);
    } catch (err) {
      const message = err instanceof Error ? err.message : "Something went wrong.";
      setError(message);
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100">
      <div className="mx-auto flex min-h-screen w-full max-w-4xl flex-col px-4 py-8">
        <header className="mb-6">
          <p className="text-sm uppercase tracking-[0.2em] text-slate-400">MCP Chat UI</p>
          <h1 className="mt-2 text-3xl font-semibold text-white">Talk to your MCP client</h1>
          <p className="mt-2 text-slate-300">
            Messages are sent to <span className="font-medium text-slate-100">POST /chat</span>.
            Configure the base URL via <code className="rounded bg-slate-800 px-2 py-0.5">VITE_MCP_BASE_URL</code>.
          </p>
        </header>

        <section className="flex-1 overflow-hidden rounded-2xl border border-slate-800 bg-slate-900/40">
          <div className="flex h-full flex-col">
            <div className="flex-1 space-y-4 overflow-y-auto px-6 py-6">
              {messages.map((message) => (
                <div
                  key={message.id}
                  className={`flex ${message.role === "user" ? "justify-end" : "justify-start"}`}
                >
                  <div
                    className={`max-w-[80%] rounded-2xl px-4 py-3 text-sm leading-relaxed shadow-sm ${
                      message.role === "user"
                        ? "bg-indigo-500 text-white"
                        : "bg-slate-800 text-slate-100"
                    }`}
                  >
                    <p className="text-xs uppercase tracking-wide text-slate-300">
                      {message.role === "user" ? "You" : "Assistant"}
                    </p>
                    <p className="mt-1 whitespace-pre-wrap">{message.content}</p>
                  </div>
                </div>
              ))}
              {isLoading && (
                <div className="flex justify-start">
                  <div className="max-w-[80%] rounded-2xl bg-slate-800 px-4 py-3 text-sm text-slate-200">
                    <p className="text-xs uppercase tracking-wide text-slate-400">Assistant</p>
                    <p className="mt-1 animate-pulse">Thinking...</p>
                  </div>
                </div>
              )}
            </div>

            <div className="border-t border-slate-800 bg-slate-950/60 px-6 py-4">
              {error && (
                <div className="mb-3 rounded-lg border border-rose-500/50 bg-rose-500/10 px-3 py-2 text-sm text-rose-200">
                  {error}
                </div>
              )}
              <form onSubmit={handleSend} className="flex flex-col gap-3 sm:flex-row sm:items-center">
                <input
                  value={input}
                  onChange={(event) => setInput(event.target.value)}
                  placeholder="Type your message..."
                  className="flex-1 rounded-xl border border-slate-700 bg-slate-900 px-4 py-3 text-sm text-slate-100 placeholder:text-slate-500 focus:border-indigo-400 focus:outline-none focus:ring-2 focus:ring-indigo-400/30"
                  disabled={isLoading}
                />
                <button
                  type="submit"
                  disabled={!canSend}
                  className="rounded-xl bg-indigo-500 px-6 py-3 text-sm font-semibold text-white transition hover:bg-indigo-400 disabled:cursor-not-allowed disabled:bg-slate-700"
                >
                  {isLoading ? "Sending..." : "Send"}
                </button>
              </form>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
