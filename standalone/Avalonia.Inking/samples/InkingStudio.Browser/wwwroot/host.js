const recoveryKey = 'avalonia-inking-studio:recovery:v1';
export function openInk() {
    return new Promise((resolve, reject) => {
        const input = document.createElement('input');
        input.type = 'file'; input.accept = '.ink.json,.json,application/json'; input.hidden = true;
        let done = false;
        const finish = (value, error) => { if (done) return; done = true; input.remove(); error ? reject(error) : resolve(value); };
        input.addEventListener('cancel', () => finish(null), { once: true });
        input.addEventListener('change', async () => {
            try {
                const file = input.files?.[0];
                if (!file) return finish(null);
                if (file.size > 128 * 1024 * 1024) throw new Error('File exceeds the browser import budget.');
                const text = await file.text();
                if (text.length > 64 * 1024 * 1024) throw new Error('Document exceeds the ink import budget.');
                finish(text);
            } catch (error) { finish(null, error); }
        }, { once: true });
        document.body.append(input); input.click();
    });
}
export function saveFile(name, base64, mimeType) {
    const binary = atob(base64);
    const bytes = Uint8Array.from(binary, character => character.charCodeAt(0));
    const url = URL.createObjectURL(new Blob([bytes], { type: mimeType }));
    const link = document.createElement('a'); link.href = url; link.download = name;
    document.body.append(link); link.click(); link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
    return true; // A browser download was initiated; the browser owns its eventual completion.
}
export function readDraft() { return localStorage.getItem(recoveryKey); }
export function writeDraft(json) {
    if (json.length > 4 * 1024 * 1024) throw new Error('Recovery draft exceeds 4 Mi characters. Save an ink file instead.');
    localStorage.setItem(recoveryKey, json);
}
