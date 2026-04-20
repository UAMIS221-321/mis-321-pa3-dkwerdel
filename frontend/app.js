const API_BASE_URL = window.TUNEFINDER_API_BASE_URL || "http://localhost:5001";
const sessionId = `session-${crypto.randomUUID()}`;

const state = {
  loading: false,
  messages: [
    {
      role: "assistant",
      text: "I am TuneFinder AI. Ask me for mood-based songs, similar artists, playlists, or artist info."
    }
  ]
};

function renderApp() {
  const app = document.getElementById("app");
  app.innerHTML = "";

  const container = document.createElement("div");
  container.className = "container py-4";

  const card = document.createElement("div");
  card.className = "card shadow-sm";

  const cardHeader = document.createElement("div");
  cardHeader.className = "card-header bg-dark text-white";
  cardHeader.innerHTML = `
    <h1 class="h5 mb-1">TuneFinder AI</h1>
    <p class="mb-0 small text-white-50">LLM + RAG + Function Calling Music Discovery Assistant</p>
  `;

  const cardBody = document.createElement("div");
  cardBody.className = "card-body";

  const chatLog = document.createElement("div");
  chatLog.className = "chat-log border rounded p-3 bg-white mb-3";

  state.messages.forEach((msg) => {
    const wrapper = document.createElement("div");
    wrapper.className = "mb-3";

    const bubble = document.createElement("div");
    bubble.className =
      msg.role === "user"
        ? "alert alert-primary mb-1"
        : "alert alert-secondary mb-1";
    bubble.textContent = msg.text;

    const label = document.createElement("small");
    label.className = "text-muted";
    label.textContent = msg.role === "user" ? "You" : "TuneFinder AI";

    wrapper.appendChild(bubble);
    wrapper.appendChild(label);
    chatLog.appendChild(wrapper);
  });

  const form = document.createElement("form");
  form.className = "d-flex gap-2";
  form.id = "chatForm";

  const input = document.createElement("input");
  input.type = "text";
  input.name = "message";
  input.className = "form-control";
  input.placeholder = "Ask for recommendations, playlists, or artist details...";
  input.required = true;
  input.disabled = state.loading;

  const button = document.createElement("button");
  button.type = "submit";
  button.className = "btn btn-dark";
  button.disabled = state.loading;
  button.textContent = state.loading ? "Thinking..." : "Send";

  form.appendChild(input);
  form.appendChild(button);

  const helper = document.createElement("p");
  helper.className = "text-muted small mt-2 mb-0";
  helper.textContent = "Demo prompts: 'Make me a workout playlist', 'Artists like SZA', 'Explain indie rock'.";

  cardBody.appendChild(chatLog);
  cardBody.appendChild(form);
  cardBody.appendChild(helper);

  card.appendChild(cardHeader);
  card.appendChild(cardBody);
  container.appendChild(card);
  app.appendChild(container);

  chatLog.scrollTop = chatLog.scrollHeight;

  form.addEventListener("submit", handleSubmit);
}

async function handleSubmit(event) {
  event.preventDefault();
  if (state.loading) {
    return;
  }

  const form = event.currentTarget;
  const input = form.elements.message;
  const message = input.value.trim();
  if (!message) {
    return;
  }

  state.messages.push({ role: "user", text: message });
  state.loading = true;
  renderApp();

  try {
    const response = await fetch(`${API_BASE_URL}/api/chat`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        sessionId,
        message
      })
    });

    if (!response.ok) {
      const errorText = await response.text();
      throw new Error(errorText || "Failed to send message");
    }

    const data = await response.json();
    state.messages.push({
      role: "assistant",
      text: data.response || "No response from assistant."
    });
  } catch (error) {
    state.messages.push({
      role: "assistant",
      text: `Error: ${error.message}`
    });
  } finally {
    state.loading = false;
    renderApp();
  }
}

renderApp();
