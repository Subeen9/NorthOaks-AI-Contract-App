window.startResizing = () => {
    const resizer = document.querySelector(".resizer");
    const chat = document.getElementById("chatPanel");
    const viewer = document.querySelector(".pdf-area");

    let startX, startWidth;

    function mouseDown(e) {
        startX = e.clientX;
        startWidth = chat.offsetWidth;
        document.addEventListener("mousemove", mouseMove);
        document.addEventListener("mouseup", mouseUp);
    }

    function mouseMove(e) {
        const totalWidth = viewer.parentElement.offsetWidth;
        const newWidth = startWidth - (e.clientX - startX);

        if (newWidth > 250 && newWidth < totalWidth - 250) {
            chat.style.width = `${newWidth}px`;
            viewer.style.width = `${totalWidth - newWidth - 5}px`; 
        }
    }

    function mouseUp() {
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
