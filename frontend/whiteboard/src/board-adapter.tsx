import { createRoot } from 'react-dom/client';
import type { ExcalidrawImperativeAPI } from '@excalidraw/excalidraw/types';
import type { ExcalidrawElement } from '@excalidraw/excalidraw/element/types';
import '@excalidraw/excalidraw/index.css';
import './classroom.css';

declare global { interface Window { EXCALIDRAW_ASSET_PATH: string; } }
window.EXCALIDRAW_ASSET_PATH = '/vendor/whiteboard/';
export type BoardScene = { elements: readonly ExcalidrawElement[]; appState: { viewBackgroundColor: string } };
const allowed = new Set(['freedraw', 'line', 'arrow', 'rectangle', 'ellipse', 'diamond', 'text']);
const key = (scene: BoardScene) => JSON.stringify([scene.appState.viewBackgroundColor, scene.elements.map(e => [e.id, e.version, e.versionNonce, e.isDeleted])]);

export async function mount(container: HTMLElement, options: { scene: BoardScene; canEdit: boolean; onSceneChange: (scene: BoardScene) => void }) {
  const { Excalidraw, MainMenu, CaptureUpdateAction, restoreElements, exportToCanvas } = await import('@excalidraw/excalidraw');
  let api: ExcalidrawImperativeAPI;
  let editable = options.canEdit, disposed = false, initialized = false;
  let current: BoardScene = { ...options.scene, elements: restoreElements(options.scene.elements, null) };
  let lastKey = key(current), generation = 0, rendered = -1;
  let frame: HTMLCanvasElement | null = null, exporting: Promise<void> | null = null;
  const root = createRoot(container);
  let ready!: () => void;
  const readyPromise = new Promise<void>(resolve => { ready = resolve; });
  async function exportFrame() {
    if (exporting) return exporting;
    exporting = (async () => {
      while (!disposed && rendered !== generation) {
        const version = generation, scene = current;
        // Fonts must be ready before the export is cached for recording.
        await document.fonts.ready;
        const next = scene.elements.some(e => !e.isDeleted)
          ? await exportToCanvas({ elements: scene.elements.filter(e => !e.isDeleted), appState: { ...scene.appState, exportBackground: true, exportWithDarkMode: false }, files: {}, maxWidthOrHeight: 1600 })
          : Object.assign(document.createElement('canvas'), { width: 1200, height: 675 });
        if (!scene.elements.some(e => !e.isDeleted)) { const c = next.getContext('2d')!; c.fillStyle = scene.appState.viewBackgroundColor; c.fillRect(0, 0, next.width, next.height); }
        if (!disposed && version === generation) { frame = next; rendered = version; }
      }
    })().finally(() => { exporting = null; });
    return exporting;
  }
  function changedFrame() { generation++; void exportFrame().catch(error => { console.error('Whiteboard export failed', error); }); }
  function fit(force = false) { if (!disposed && api && container.getBoundingClientRect().width) { api.refresh(); if (!editable || force) api.scrollToContent(undefined, { fitToContent: true, animate: false }); } }
  function render() {
    root.render(<Excalidraw
      initialData={{ ...current, scrollToContent: true }}
      viewModeEnabled={!editable}
      handleKeyboardGlobally={false}
      UIOptions={{ tools: { image: false }, canvasActions: { loadScene: false, saveToActiveFile: false, export: false, saveAsImage: false, changeViewBackgroundColor: false } }}
      onPaste={() => false}
      onLinkOpen={(_, event) => event.preventDefault()}
      validateEmbeddable={() => false}
      excalidrawAPI={value => { api = value; if (!initialized) { initialized = true; ready(); } }}
      onChange={(elements, state) => {
        if (disposed) return;
        const scene: BoardScene = { elements, appState: { viewBackgroundColor: state.viewBackgroundColor } };
        if (key(scene) === lastKey) return;
        if (!editable || elements.some(e => !allowed.has(e.type) || e.link || e.customData)) {
          api.updateScene({ ...current, captureUpdate: CaptureUpdateAction.NEVER });
          api.setToast({ message: 'در این کلاس فقط قلم، شکل، فلش و متن مجاز است.' });
          return;
        }
        current = scene; lastKey = key(scene); changedFrame(); options.onSceneChange(structuredClone(scene));
      }}
    ><MainMenu>{editable && <MainMenu.DefaultItems.ClearCanvas />}</MainMenu></Excalidraw>);
  }
  render(); await readyPromise;
  const observer = new ResizeObserver(() => fit(true)); observer.observe(container);
  changedFrame(); fit();
  return {
    applyScene(scene: BoardScene) {
      current = { ...scene, elements: restoreElements(scene.elements, null) }; lastKey = key(current);
      api.updateScene({ ...current, captureUpdate: CaptureUpdateAction.NEVER }); api.history.clear(); changedFrame(); fit();
    },
    setEditable(enabled: boolean) { if (editable === enabled) return; editable = enabled; render(); },
    resize: () => fit(true),
    getRecordingFrame: () => frame,
    flushRecordingFrame: async () => { await exportFrame(); if (!frame) throw new Error('تصویر تخته برای ضبط آماده نیست.'); },
    dispose() { disposed = true; observer.disconnect(); root.unmount(); frame = null; },
  };
}
