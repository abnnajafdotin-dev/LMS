import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import '@excalidraw/excalidraw/index.css';
import './styles.css';

declare global {
  interface Window { EXCALIDRAW_ASSET_PATH: string; }
}

// Set before loading the editor: its font loader otherwise falls back to a CDN.
window.EXCALIDRAW_ASSET_PATH = '/vendor/whiteboard/';

async function start() {
  const { Excalidraw, MainMenu } = await import('@excalidraw/excalidraw');
  function WhiteboardPreview() {
    const [viewOnly, setViewOnly] = useState(false);
    useEffect(() => {
      const toggle = document.getElementById('toggle-view') as HTMLButtonElement;
      toggle.onclick = () => setViewOnly(value => !value);
      toggle.textContent = viewOnly ? 'فعال‌کردن حالت ویرایش' : 'فعال‌کردن حالت مشاهده';
      return () => { toggle.onclick = null; };
    }, [viewOnly]);
    return <Excalidraw
      viewModeEnabled={viewOnly}
      initialData={{ elements: [], appState: { viewBackgroundColor: '#ffffff' } }}
      UIOptions={{ tools: { image: false }, canvasActions: { loadScene: false, saveToActiveFile: false, export: false } }}
      excalidrawAPI={() => { document.getElementById('status')!.textContent = 'تخته آماده است'; }}
    >
      <MainMenu><MainMenu.DefaultItems.ClearCanvas /></MainMenu>
    </Excalidraw>;
  }
  createRoot(document.getElementById('whiteboard-root')!).render(<WhiteboardPreview />);
}

start().catch(error => {
  document.getElementById('status')!.textContent = 'بارگذاری تخته ناموفق بود؛ صفحه را دوباره باز کنید.';
  console.error(error);
});
