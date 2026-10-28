(() => {
    const widget = document.querySelector(".assistant-chat");
    if (!widget) return;

    const toggle = widget.querySelector(".assistant-chat-toggle");
    const panel = widget.querySelector(".assistant-chat-panel");
    const close = widget.querySelector(".assistant-chat-close");
    const form = widget.querySelector(".assistant-chat-form");
    const input = widget.querySelector("#assistant-chat-input");
    const sendButton = form.querySelector("button");
    const messagesElement = widget.querySelector(".assistant-chat-messages");
    const conversation = [];
    let sending = false;

    const setOpen = (open) => {
        panel.hidden = !open;
        toggle.setAttribute("aria-expanded", String(open));
        if (open) input.focus();
        else toggle.focus();
    };

    const addMessage = (content, role) => {
        const message = document.createElement("p");
        message.className = `assistant-chat-message ${role === "user" ? "assistant-chat-user" : "assistant-chat-reply"}`;
        message.textContent = content;
        messagesElement.append(message);
        messagesElement.scrollTop = messagesElement.scrollHeight;
    };

    toggle.addEventListener("click", () => setOpen(panel.hidden));
    close.addEventListener("click", () => setOpen(false));

    form.addEventListener("submit", async (event) => {
        event.preventDefault();
        const content = input.value.trim();
        if (!content || sending) return;

        while (conversation.length > 10) conversation.splice(0, 2);
        conversation.push({ role: "user", content });
        addMessage(content, "user");
        input.value = "";
        sending = true;
        sendButton.disabled = true;
        input.disabled = true;

        try {
            const response = await fetch("/api/chat", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ messages: conversation })
            });
            const result = await response.json();
            if (!response.ok) {
                throw new Error(result.detail || result.error || "The assistant could not respond. Please try again.");
            }

            conversation.push({ role: "assistant", content: result.reply });
            addMessage(result.reply, "assistant");
        } catch (error) {
            addMessage(error.message || "The assistant could not respond. Please try again.", "assistant");
            conversation.pop();
        } finally {
            sending = false;
            sendButton.disabled = false;
            input.disabled = false;
            input.focus();
        }
    });
})();
