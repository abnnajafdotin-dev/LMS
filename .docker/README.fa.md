# میزبانی با Docker

این تنظیم برای یک سرور Linux با IP عمومی و Docker Engine به‌همراه Compose v2 است. بک‌اند، تخته Excalidraw، LiveKit و Caddy را اجرا می‌کند و Caddy گواهی HTTPS را خودکار می‌گیرد. برای build به اینترنت نیاز است؛ Node و SDK روی میزبان لازم نیستند.

## راه‌اندازی

از ریشه پروژه:

```sh
cp .docker/.env.example .docker/.env
```

در ویندوز دستور معادل `Copy-Item .docker/.env.example .docker/.env` است.

فایل `.docker/.env` را ویرایش کنید:

- `APP_DOMAIN`: دامنه برنامه، مانند `class.example.com`، بدون پروتکل و مسیر.
- `LIVEKIT_DOMAIN`: دامنه جداگانه LiveKit، مانند `livekit.example.com`.
- `SERVER_IP`: IPv4 عمومی سرور؛ آدرس نمونه باید جایگزین شود.
- `LIVEKIT_API_KEY`: کلید دلخواه با حروف و اعداد.
- `LIVEKIT_API_SECRET`: مقدار تصادفی با حداقل ۳۲ کاراکتر؛ برای نمونه خروجی `openssl rand -hex 32`.
- `PRESENTER_CODE`: کد ورود مدرس با مقدار قوی و غیرنمونه.

رکورد A هر دو دامنه را روی IP سرور قرار دهید. پورت‌های TCP برابر 80، 443 و 7881 و UDP برابر 7882 را در فایروال سرور و ارائه‌دهنده باز کنید. در صورت NAT، همین پورت‌ها با همان شماره به سرور هدایت شوند. رکورد دامنه LiveKit باید DNS-only باشد تا ترافیک رسانه مستقیم به سرور برسد. پورت‌های 7880 و 8080 فقط داخل شبکه Docker استفاده می‌شوند.

```sh
docker compose --env-file .docker/.env -f .docker/compose.yml config --quiet
docker compose --env-file .docker/.env -f .docker/compose.yml up -d --build
docker compose --env-file .docker/.env -f .docker/compose.yml ps
```

سپس `https://class.example.com` را باز کنید. برای دوربین و میکروفون، HTTPS لازم است. وضعیت بک‌اند و اتصال LiveKit در `https://class.example.com/api/health` قابل مشاهده است؛ مقدار `livekit` باید `true` باشد. با دو دستگاه ورود به کلاس، صدا، تصویر و تخته را بررسی کنید.

## نگهداری

```sh
# مشاهده لاگ‌ها
docker compose --env-file .docker/.env -f .docker/compose.yml logs -f --tail=100
# ساخت و اجرای نسخه جدید پس از دریافت تغییرات پروژه
docker compose --env-file .docker/.env -f .docker/compose.yml up -d --build
# توقف بدون حذف داده‌ها
docker compose --env-file .docker/.env -f .docker/compose.yml down
```

وضعیت تخته‌ها و فایل‌های سروری در volume با نام `training-lms_app-data` نگهداری می‌شود؛ از آن نسخه پشتیبان بگیرید. گواهی‌ها در volume مربوط به Caddy هستند. دستور `down -v` داده‌ها و گواهی‌ها را حذف می‌کند. نشست‌های ورود حافظه‌ای‌اند و با restart برنامه، ورود مجدد لازم می‌شود.

این بسته‌بندی، محدودیت‌های PoC را تغییر نمی‌دهد: کد مدرس جایگزین احراز هویت سازمانی نیست و تست ظرفیت انجام نشده است. این تنظیم تک‌سروری TURN ندارد؛ برای کاربرانی با شبکه محدود که TCP/UDP مستقیم را مسدود می‌کنند، TURN را جداگانه تنظیم کنید. جزئیات شبکه در [مستندات LiveKit](https://docs.livekit.io/transport/self-hosting/ports-firewall/) آمده است.
