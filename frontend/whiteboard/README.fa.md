# تخته Excalidraw کلاس

تخته کلاس در صفحه اصلی به نشست، API نسخه‌دار، LiveKit و ضبط متصل است. فقط مدرس فعال می‌تواند ویرایش کند؛ مجوز در سرور نیز بررسی می‌شود. قلم، شکل، فلش، متن فارسی و undo/redo پشتیبانی می‌شوند. تصویر، فایل، embed، لینک و همکاری هم‌زمان چند نویسنده در این MVP نیستند.

## اجرا و build

از ریشه مخزن روی Windows x64، اولین بار:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/setup.ps1
```

setup اصلی، setup تخته را هم اجرا می‌کند. برای ساخت فقط تخته:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/setup-whiteboard.ps1
```

این دستور Node 22.23.4 را با بررسی checksum در `.tools` آماده می‌کند، `npm ci` و بررسی TypeScript و build را اجرا می‌کند. نصب سراسری لازم نیست. برای build بعدی بدون نصب مجدد:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-whiteboard.ps1
```

با اجرای `START-DEMO.cmd`، کلاس را در `http://localhost:5080` باز کنید. پس از build، اجرای کلاس فقط به .NET و LiveKit نیاز دارد و Node یا CDN لازم نیست. صفحه `/vendor/whiteboard/index.html` همچنان یک آزمایش مستقل و بدون ذخیره است؛ ورود به کلاس از صفحه اصلی انجام می‌شود.

خروجی `src/Lms.Poc/wwwroot/vendor/whiteboard/` و `node_modules` در Git ذخیره نمی‌شوند. سورس و lockfile مبنای بازسازی‌اند. build فقط پوشه اختصاصی whiteboard را جایگزین می‌کند؛ برای انتشار، خروجی build باید در artifact قرار گیرد. فونت‌ها و `THIRD-PARTY-NOTICES.txt` همراه خروجی هستند.

## ذخیره و رفع خطا

scene از HTTP ذخیره می‌شود؛ LiveKit فقط اعلان revision می‌فرستد و polling سه‌ثانیه‌ای اعلان ازدست‌رفته را جبران می‌کند. درخواست‌های ذخیره سریالی‌اند؛ تغییرات پیوسته با سقف انتظار دو ثانیه ارسال می‌شوند. سقف Snapshot برابر 1 MiB، تعداد عناصر ۱۰۰۰ شامل حذف‌شده‌ها و نقاط هر freedraw برابر ۲۰۰۰ است. تاریخچه undo محلی است و با refresh برنمی‌گردد.

در 400/413، تغییر محلی حفظ و retry خودکار متوقف می‌شود؛ محتوا را اصلاح یا کپی تغییرات را دریافت کنید. در خطای شبکه، ویرایش متوقف و پس از بازیابی اتصال ذخیره دوباره تلاش می‌شود. در 403 ورود مجدد لازم است. در 409 وضعیت سرور بازیابی می‌شود و کپی تغییرات قبلی برای دریافت نگه داشته می‌شود. فایل دریافت تغییرات، نسخه پشتیبان JSON است؛ import از این فایل هنوز در رابط کلاس پیاده نشده است. «کنارگذاشتن تغییرات و خروج» تغییرات تأییدنشده را کنار می‌گذارد؛ پیش از آن می‌توانید نسخه محلی را دریافت کنید.

## مهاجرت و بازگشت

فایل‌های قدیمی `App_Data/<room>.json` هنگام اولین دسترسی شناخته و پس از اعتبارسنجی به v2 تبدیل می‌شوند. پیش از جایگزینی، کپی دقیق با نام `<room>.json.legacy-<id>.bak` ساخته می‌شود. revision، رنگ، ضخامت و مختصات حفظ می‌شوند. داده خراب یا schema ناشناخته خطای روشن می‌دهد و خودکار پاک نمی‌شود.

برای rollback: دمو را متوقف کنید، از کل `App_Data` فعلی کپی بگیرید، کد قبلی را بازیابی و backup قدیمی اتاق را به جای JSON همان اتاق قرار دهید، سپس دمو را راه‌اندازی کنید. sceneهای جدید v2 در کد قدیمی قابل نمایش نیستند؛ backup فعلی را نگه دارید. پس از اصلاح فایل خراب نیز بک‌اند را restart کنید، زیرا وضعیت اتاق در حافظه cache می‌شود.

## بررسی

با سرورهای روشن:

```powershell
$env:DOTNET_CLI_HOME = "$PWD/.tools/cli"
& ./.tools/dotnet/dotnet.exe run --project tests/Whiteboard
& ./.tools/dotnet/dotnet.exe run --project tests/WhiteboardApi
& ./.tools/dotnet/dotnet.exe run --project tests/Smoke
& ./.tools/node-v22.23.4-win-x64/node.exe tests/whiteboard-browser.mjs
```

تست مرورگر آخر به Chrome نیاز دارد؛ مسیر پیش‌فرض Windows است و با `CHROME_PATH` قابل تغییر است. تست API برای انقضای lease حدود ۳۱ ثانیه منتظر می‌ماند. شبیه‌سازی خطاهای شبکه در مرورگر جایگزین تست قطع واقعی شبکه نیست. [نتایج و محدودیت‌ها](../../docs/EXCALIDRAW-VERIFICATION.fa.md).
