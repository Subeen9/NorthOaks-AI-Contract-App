window.startResizing = () => {
    const resizer = document.querySelector(".resizer");
    const chat = document.getElementById("chatPanel");
    const viewer = document.querySelector(".pdf-area");
    const container = viewer.parentElement;

    let startX, startWidth, containerWidth;

    function mouseDown(e) {
        startX = e.clientX;
        startWidth = chat.offsetWidth;
        containerWidth = container.offsetWidth;

        resizer.classList.add("resizing");
        document.body.style.cursor = "ew-resize";
        document.body.style.userSelect = "none";

        const iframe = document.querySelector(".pdf-frame");
        if (iframe) iframe.style.pointerEvents = "none";

        document.addEventListener("mousemove", mouseMove);
        document.addEventListener("mouseup", mouseUp);

        e.preventDefault();
    }

    function mouseMove(e) {
        const delta = startX - e.clientX;
        const newWidth = startWidth + delta;

        const minWidth = 280;
        const maxWidth = containerWidth * 0.5;

        if (newWidth >= minWidth && newWidth <= maxWidth) {
            chat.style.width = `${newWidth}px`;
            viewer.style.flex = "1 1 auto";
        }

        e.preventDefault();
    }

    function mouseUp() {
        resizer.classList.remove("resizing");
        document.body.style.cursor = "";
        document.body.style.userSelect = "";

        const iframe = document.querySelector(".pdf-frame");
        if (iframe) iframe.style.pointerEvents = "";

        document.removeEventListener("mousemove", mouseMove);
        document.removeEventListener("mouseup", mouseUp);
    }

    resizer.addEventListener("mousedown", mouseDown);
};

window.scrollChatToBottom = () => {
    const chatBox = document.getElementById("chatMessages");
    if (chatBox) {
        chatBox.scrollTop = chatBox.scrollHeight;
    }
};

window.autoResizeTextarea = () => {
    const textarea = document.querySelector('.chat-input-field');
    if (textarea) {
        textarea.style.height = 'auto';
        textarea.style.height = Math.min(textarea.scrollHeight, 200) + 'px';
    }
};

window.resetTextareaHeight = () => {
    const textarea = document.querySelector('.chat-input-field');
    if (textarea) {
        textarea.style.height = '44px';
    }
};