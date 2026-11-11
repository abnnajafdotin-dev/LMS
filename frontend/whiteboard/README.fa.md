# تخته مستقل — فاز اول

صفحه آزمایشی React/Excalidraw، بدون اتصال به نشست، API تخته، LiveKit یا ضبط. محتوای آن در حافظه است. تغییر حالت مشاهده فقط برای آزمایش UI است و مجوز امنیتی محسوب نمی‌شود.

از ریشه مخزن روی Windows x64:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/setup-whiteboard.ps1
```

این دستور Node 22.23.4 را در `.tools` با checksum رسمی آماده می‌کند، `npm ci` و سپس بررسی TypeScript و build را انجام می‌دهد. نصب سراسری لازم نیست. برای build بعدی بدون نصب مجدد:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-whiteboard.ps1
```

با اجرای `START-DEMO.cmd` صفحه زیر را باز کنید:

http://localhost:5080/vendor/whiteboard/index.html

خروجی در `src/Lms.Poc/wwwroot/vendor/whiteboard/` ساخته می‌شود و در Git ذخیره نمی‌شود. بعد از clone ابتدا `scripts/setup-whiteboard.ps1` را اجرا کنید؛ setup اصلی دمو هنوز این مرحله را اجرا نمی‌کند. پس از build، برای باز کردن صفحه Node یا CDN لازم نیست. سورس و `package-lock.json` در Git نگهداری می‌شوند و نصب مجدد باید با `npm ci` باشد. build فقط پوشه اختصاصی whiteboard را جایگزین می‌کند. فونت‌های بسته با مسیر محلی بارگذاری و متن مجوزهای وابستگی‌های نصب‌شده در `THIRD-PARTY-NOTICES.txt` جمع‌آوری می‌شود. در انتشار آینده خروجی باید پیش از بسته‌بندی سایت ساخته و در artifact انتشار گنجانده شود.

برای بازبینی: قلم، شکل و متن فارسی رسم کنید، حالت مشاهده را فعال کنید و اندازه پنجره را تغییر دهید. refresh باید صفحه خالی ایجاد کند. Network مرورگر نباید فونت یا script خارجی بگیرد. هنوز نباید این صفحه به‌عنوان تخته کلاس معرفی شود؛ API v2، ذخیره و adapter در گام‌های بعدی‌اند.

مرجع تنظیم asset محلی: [مستندات رسمی نصب](https://plus.excalidraw.com/docs/sdk/excalidraw/installation).
