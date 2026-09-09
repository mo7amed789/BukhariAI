# BukhariAI Production Deployment Guide 🚀
# دليل نشر وتشغيل نظام دِراية AI في بيئة الإنتاج

---

## 📦 حزم الإصدار الجاهزة للنشر (Release Packages)

تم بناء وتجهيز إصدارات الإنتاج في المجلد `publish/` بثلاث صيغ تناسب مختلف بيئات الاستضافة:

| الحزمة (Package) | المسار (Path) | الوصف (Description) | المتطلبات |
| :--- | :--- | :--- | :--- |
| **Framework-Dependent** | `publish/framework-dependent/` | حزمة مرنة تعمل على أي نظام (Linux/Windows/macOS) | يتطلب تثبيت [.NET 10 Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Linux Self-Contained** | `publish/linux-x64/` | حزمة مخصصة لخوادم Linux x64 مدمج بها الـ Runtime | لا تتطلب تثبيت .NET على السيرفر |
| **Windows Self-Contained** | `publish/win-x64/` | حزمة مخصصة لخوادم Windows x64 مدمج بها الـ Runtime | لا تتطلب تثبيت .NET على السيرفر |
| **Docker Container** | `Dockerfile` & `docker-compose.yml` | حاوية معزولة جاهزة للنشر الفوري | يتطلب Docker & Docker Compose |

---

## ⚙️ إعدادات المتغيرات البيئية (Environment Configuration)

يمكن ضبط الإعدادات إما عبر تعديل `appsettings.Production.json` أو عبر المتغيرات البيئية (Environment Variables) وهي الطريقة الموصى بها في بيئات السحاب والـ Containers:

| المتغير البيئي (Env Variable) | الخاصية المقابلة في JSON | الوصف | مثال |
| :--- | :--- | :--- | :--- |
| `ConnectionStrings__DefaultConnection` | `ConnectionStrings:DefaultConnection` | نص الاتصال بقاعدة بيانات SQL Server | `Server=db-host,1433;Database=BukhariAIDb;User Id=sa;Password=YourStrongPassword;TrustServerCertificate=True;` |
| `AI__ApiKey` | `AI:ApiKey` | مفتاح Google Gemini API للذكاء الاصطناعي | `AIzaSy...` |
| `AI__Provider` | `AI:Provider` | مزود الذكاء الاصطناعي (Gemini / OpenAI) | `Gemini` |
| `AI__Model` | `AI:Model` | اسم الموديل المستخدم | `gemini-3.6-flash` |
| `AI__FallbackApiKey` | `AI:FallbackApiKey` | مفتاح مزود الطوارئ (اختياري) | `sk-...` |
| `Jwt__SecretKey` | `Jwt:SecretKey` | مفتاح تشفير التوكن (يجب ألا يقل عن 32 حرفاً) | `StrongSecretKeyMin32CharactersLong!` |
| `EnableSwagger` | `EnableSwagger` | تفعيل صفحة توثيق Swagger في الإنتاج | `true` أو `false` |
| `DisableHttpsRedirection` | `DisableHttpsRedirection` | تعطيل إعادة التوجيه لـ HTTPS عند العمل خلف Reverse Proxy | `true` (عند استخدام Nginx/Cloudflare) |

---

## 🐳 الخيار الأول: النشر عبر Docker & Docker Compose (الأسرع والأسهل)

يتضمن المشروع ملف `docker-compose.yml` جاهزاً يُطلق تلقائياً حاوية التطبيق وحاوية قاعدة بيانات SQL Server 2022.

### 1. إعداد المتغيرات:
قم بنسخ ملف `.env.example` إلى `.env`:
```bash
cp .env.example .env
```
وقم بتعديل القيم بداخله (خاصة `AI_API_KEY` و `JWT_SECRET_KEY` و `MSSQL_SA_PASSWORD`).

### 2. تشغيل الحاويات:
```bash
docker compose up -d --build
```

### 3. التحقق والمراقبة:
- الدخول إلى لوحة التحكم والواجهة: `http://localhost:8080/`
- توثيق الـ API (Swagger): `http://localhost:8080/swagger`
- متابعة السجلات: `docker compose logs -f bukhariapp`

---

## 🐧 الخيار الثاني: النشر على سيرفر Linux VPS (Ubuntu / Debian)

### 1. رفع الحزمة:
قم برفع محتويات مجلد `publish/linux-x64/` إلى مسار التطبيق على السيرفر (مثلاً `/var/www/bukhariai`):
```bash
scp -r publish/linux-x64/* user@your-server-ip:/var/www/bukhariai/
```

### 2. تثبيت التبعيات النظامية وإعطاء صلاحيات التشغيل:
```bash
sudo apt update
sudo apt install -y libfontconfig1 libfreetype6
sudo chmod +x /var/www/bukhariai/BukhariAI.Api
sudo chown -R www-data:www-data /var/www/bukhariai
```

### 3. إنشاء خدمة Systemd (`/etc/systemd/system/bukhariai.service`):
```ini
[Unit]
Description=BukhariAI Service
After=network.target

[Service]
WorkingDirectory=/var/www/bukhariai
ExecStart=/var/www/bukhariai/BukhariAI.Api
Restart=always
RestartSec=10
KillSignal=SIGINT
SyslogIdentifier=bukhariai
User=www-data
Environment=ASPNETCORE_ENVIRONMENT=Production
Environment=ASPNETCORE_URLS=http://127.0.0.1:5000
Environment=ConnectionStrings__DefaultConnection="Server=127.0.0.1;Database=BukhariAIDb;User Id=sa;Password=YourStrongPassword;TrustServerCertificate=True;"
Environment=AI__ApiKey="your_gemini_api_key"
Environment=Jwt__SecretKey="YourStrongSecretKeyAtLeast32CharactersLong!"
Environment=DisableHttpsRedirection=true

[Install]
WantedBy=multi-user.target
```

تفعيل الخدمة وتشغيلها:
```bash
sudo systemctl daemon-reload
sudo systemctl enable bukhariai
sudo systemctl start bukhariai
sudo systemctl status bukhariai
```

### 4. إعداد Nginx كـ Reverse Proxy مع شهادة SSL:
في ملف `/etc/nginx/sites-available/bukhariai`:
```nginx
server {
    listen 80;
    server_name yourdomain.com;

    client_max_body_size 50M;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```
تفعيل الرابط وإعادة تشغيل Nginx:
```bash
sudo ln -s /etc/nginx/sites-available/bukhariai /etc/nginx/sites-enabled/
sudo nginx -t
sudo systemctl reload nginx
sudo certbot --nginx -d yourdomain.com
```

---

## 🪟 الخيار الثالث: النشر على Windows Server / IIS

1. قم بتثبيت **ASP.NET Core Hosting Bundle** المناسب لـ .NET 10 على السيرفر.
2. أنشئ موقعاً جديداً في IIS (New Website) ووجّه الـ Physical Path إلى مسار حزمة `publish/framework-dependent` أو `publish/win-x64`.
3. اضبط الـ Application Pool على:
   - **.NET CLR Version**: `No Managed Code`
   - **Managed Pipeline Mode**: `Integrated`
4. تأكد من ضبط الصلاحيات لمستخدم `IIS_IUSRS` للقراءة والكتابة على مجلد التطبيق.
5. ضع بيانات الاتصال والمفاتيح إما في `appsettings.Production.json` أو عبر Configuration Editor في IIS.

---

## 💾 قاعدة البيانات والترحيلات التلقائية (Database Migrations)

- يقوم التطبيق تلقائياً عند أول تشغيل بالاتصال بقاعدة البيانات عبر `db.Database.Migrate()`، وإنشاء الجداول وتطبيق كافة الـ Migrations التراكمية، بالإضافة إلى تنظيف وتهيئة سجلات المفاهيم عبر `ConceptCleanupService`.
- لا يلزم تشغيل أوامر يدوية لإنشاء الجداول إلا إذا رغبت في تصدير سكربت SQL يدوياً:
  ```bash
  dotnet ef migrations script --project src/BukhariAI.Infrastructure --startup-project src/BukhariAI.Api -o database_setup.sql
  ```
