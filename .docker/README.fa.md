# میزبانی با Docker

این تنظیم بک‌اند، تخته Excalidraw، LiveKit و Caddy را با Docker Compose اجرا می‌کند. Nginx گواهی HTTPS را ارائه می‌کند و درخواست‌ها را از طریق HTTP به Caddy می‌فرستد. برای build به اینترنت نیاز است؛ Node و SDK روی میزبان لازم نیستند.

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

رکورد A هر دو دامنه را روی IP عمومی Nginx قرار دهید. پورت‌های `80/TCP` و `443/TCP` به Nginx و پورت‌های `7881/TCP` و `7882/UDP` به میزبان Docker هدایت شوند. `SERVER_IP` باید IPv4 عمومی‌ای باشد که پورت‌های رسانه روی آن به LiveKit می‌رسند. دسترسی به `5010/TCP` روی میزبان Docker را به Nginx محدود کنید.

فایل `nginx.conf` این پوشه را جایگزین تنظیمات فعلی این دو دامنه در `/etc/nginx/conf.d/` کنید تا `map` در سطح `http` بارگذاری شود. در صورت نیاز IP داخلی میزبان Docker، نام دامنه‌ها و مسیر گواهی‌ها را در آن تنظیم کنید. سپس `sudo nginx -t && sudo systemctl reload nginx` را اجرا کنید.

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

وضعیت تخته‌ها و فایل‌های سروری در volume با نام `training-lms_app-data` نگهداری می‌شود؛ از آن نسخه پشتیبان بگیرید. دستور `down -v` داده‌های برنامه را حذف می‌کند. نشست‌های ورود حافظه‌ای‌اند و با restart برنامه، ورود مجدد لازم می‌شود.

این بسته‌بندی، محدودیت‌های PoC را تغییر نمی‌دهد: کد مدرس جایگزین احراز هویت سازمانی نیست و تست ظرفیت انجام نشده است. این تنظیم تک‌سروری TURN ندارد؛ برای کاربرانی با شبکه محدود که TCP/UDP مستقیم را مسدود می‌کنند، TURN را جداگانه تنظیم کنید. جزئیات شبکه در [مستندات LiveKit](https://docs.livekit.io/transport/self-hosting/ports-firewall/) آمده است.
