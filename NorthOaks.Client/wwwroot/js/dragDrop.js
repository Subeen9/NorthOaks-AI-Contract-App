window.initDragDrop = () => {
    const dropArea = document.getElementById('dropArea');
    const hiddenInput = document.getElementById('hiddenInputFile');

    if (!dropArea || !hiddenInput) return;

    let dragOver = false;

    // Prevent default browser behavior
    ['dragenter', 'dragover', 'dragleave', 'drop'].forEach(eventName => {
        dropArea.addEventListener(eventName, (e) => e.preventDefault());
    });

    // Highlight on drag
    dropArea.addEventListener('dragenter', () => {
        dragOver = true;
        dropArea.classList.add('bg-light');
        dropArea.querySelector('p').innerText = "Drop your PDF here";
    });

    dropArea.addEventListener('dragleave', () => {
        dragOver = false;
        dropArea.classList.remove('bg-light');
        dropArea.querySelector('p').innerText = "Drag & drop a PDF here";
    });

    // Handle drop
    dropArea.addEventListener('drop', (e) => {
        e.preventDefault();
        dragOver = false;
        dropArea.classList.remove('bg-light');
        dropArea.querySelector('p').innerText = "Drag & drop a PDF here";

        const dt = e.dataTransfer;
        if (!dt || dt.files.length === 0) return;

        const dataTransfer = new DataTransfer();
        for (let i = 0; i < dt.files.length; i++) {
            dataTransfer.items.add(dt.files[i]);
        }

        hiddenInput.files = dataTransfer.files;
        hiddenInput.dispatchEvent(new Event('change', { bubbles: true }));
    });

    // Click to open file dialog
    dropArea.addEventListener('click', () => {
        hiddenInput.click();
    });
};
