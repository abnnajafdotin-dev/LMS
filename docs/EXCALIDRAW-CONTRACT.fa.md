# قرارداد پیشنهادی تخته v2

وضعیت: پیاده‌سازی شده برای Excalidraw 0.18.1. کلاینت قدیمی strokes با خطای 400 رد می‌شود.

## ساختار

GET احرازشده `/api/board`:

```json
{
  "schemaVersion": 2,
  "engine": "excalidraw",
  "revision": 0,
  "scene": {
    "elements": [],
    "appState": { "viewBackgroundColor": "#ffffff" }
  }
}
```

PUT احرازشده همان مسیر:

```json
{
  "schemaVersion": 2,
  "engine": "excalidraw",
  "baseRevision": 0,
  "scene": {
    "elements": [],
    "appState": { "viewBackgroundColor": "#ffffff" }
  }
}
```

`elements` اشیای کامل معتبر نسخه pin شده Excalidraw هستند؛ نمونه خالی بالا جایگزین schema عناصر نیست. عناصر حذف‌شده تا زمان لازم برای undo و نسخه انتخابی حفظ و در سقف تعداد محاسبه شوند. `files` در MVP مجاز نیست. appState فقط فهرست سفید بالا دارد؛ scroll، zoom، selection، collaborators و تاریخچه محلی مشترک نیستند.

## قواعد سرور

- نشست نامعتبر: 401؛ فراگیر یا مدرس فاقد lease معتبر: 403.
- بررسی مجوز و revision و نوشتن زیر قفل اتاق انجام شود؛ revision سرور یکنواخت افزایش یابد.
- schema/engine نامعتبر، element نامعتبر یا نوع غیرمجاز: 400 همراه پیام روشن؛ درخواست v1 بی‌صدا نادیده گرفته نشود.
- revision ناسازگار: 409 با snapshot کامل v2؛ ذخیره انجام نشود.
- پیشنهاد سقف اولیه: snapshot سریال‌شده حداکثر 1 MiB، بدنه PUT حداکثر 1100 KiB، 1000 عنصر شامل حذف‌شده‌ها و 2000 نقطه برای هر freedraw. این اعداد تصمیم MVP هستند و باید با نمونه واقعی ارزیابی شوند؛ سقف فعلی 128 KiB مستقیماً قابل استفاده نیست.
- محدودیت Kestrel فعلی 160 KiB است؛ محدودیت مخصوص مسیر board تغییر کند و مسیرهای دیگر، به‌خصوص recordings، جدا بررسی شوند.
- اعتبارسنجی عمق JSON، شناسه یکتا، مقادیر عددی محدود و متناهی، طول متن حداکثر 10000 نویسه، رنگ معتبر و روابط عناصر انجام شود. مختصات و ابعاد قدرمطلق حداکثر 100000 باشند. سقف‌ها پیش از نوشتن اعمال شوند.
- انواع مجاز اولیه: freedraw، line، arrow، rectangle، ellipse، diamond و text؛ image، embeddable، iframe و هر نوع ناشناخته رد شوند. link غیرخالی، customData و asset خارجی رد شوند. روابط binding/group با نسخه منتخب تطبیق داده شوند.
- ذخیره فایل موقت و جایگزینی اتمیک؛ سپس اعلان `board.changed` با `{revision}`. شکست اعلان ذخیره موفق را لغو نمی‌کند؛ پاسخ `{revision, notified}` حفظ شود.
- schema ناشناخته روی دیسک خطای روشن بدهد؛ هیچ fallback به تخته خالی و overwrite مجاز نیست.

## رفتار کلاینت در خطا

409: کپی تغییر محلی موقت حفظ، editor تا بازیابی متوقف و snapshot سرور اعمال شود؛ پیام از دست رفتن تأیید تغییر به مدرس نمایش داده شود. retry کور روی baseRevision جدید ممنوع است.

413/400: تغییر محلی حفظ و وضعیت «ذخیره نشده» نمایش داده شود؛ ارسال خودکار بی‌پایان انجام نشود. مدرس بتواند محتوا را کم کند و دوباره ذخیره کند.

خطای شبکه/5xx: صف آخرین scene حفظ و با تأخیر محدود retry شود؛ هنگام قطع اتصال یا backendReady=false ویرایش مسدود شود. 403 و پایان نشست نیازمند ورود مجدد است. پاسخ‌های مربوط به نشست یا اتاق قبلی پس از خروج اعمال نشوند.

## مرز adapter

این قرارداد متعلق به برنامه میزبان است، نه API مستقیم Excalidraw:

```ts
mount(container, { scene, canEdit, onSceneChange }): Promise<BoardAdapter>
interface BoardAdapter {
  applyScene(scene: BoardScene): void; // بدون بازنشر onSceneChange
  setEditable(enabled: boolean): void;
  resize(): void;
  getRecordingFrame(): HTMLCanvasElement | null; // cache آماده، بدون export در هر فریم
  flushRecordingFrame(): Promise<void>;
  dispose(): void;
}
```

تعریف BoardScene مطابق scene بالا و typeهای نسخه pin شده خواهد بود. fetch و token در میزبان می‌مانند؛ adapter مالک API شبکه یا LiveKit نیست. undo/redo داخلی editor است؛ اعمال remote snapshot نباید وارد تاریخچه ویرایش فراگیر شود.
