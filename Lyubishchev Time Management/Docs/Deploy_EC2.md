# 在 AWS EC2 部署 Lyubishchev Time Management

> **適用範圍：** 單一 Ubuntu 24.04 LTS EC2 執行 Nginx、ASP.NET Core 10/Kestrel 與 MySQL 8。這是本專案 V1 所定義的「單一 EC2 monolith」架構；資料庫不對 Internet 開放。

## 1. 部署目標與既有環境

```text
Internet
  -> AWS Security Group (80, 443)
  -> Nginx :80/:443
  -> Kestrel 127.0.0.1:5000
  -> MySQL 127.0.0.1:3306
```

本專案為 ASP.NET Core MVC（`net10.0`）、EF Core 10、`MySql.EntityFrameworkCore` 與 MySQL/InnoDB；前端由 Razor Views 與原生 JavaScript 組成。JWT 以名稱 `ltm_auth` 的 Secure、HttpOnly、SameSite=Strict Cookie 保存，故正式環境必須提供有效 HTTPS。資料庫 migration 已存在於 `Data/Migrations/`，設定項目為：

- `ConnectionStrings:DefaultConnection`
- `Jwt:Issuer`（目前值 `LyubishchevTimeManagement`）
- `Jwt:Audience`（目前值 `LyubishchevTimeManagement`）
- `Jwt:SigningKey`（至少 32 個高熵字元；不得放入 Git）
- `Jwt:ExpirationHours`（目前預設 8）

本文件選擇 **原生 .NET Runtime + systemd**，而不是目前僅供 Visual Studio 容器偵錯使用的 Dockerfile，因為 `AGENTS.md` 明確指定以 systemd 管理正式環境的 ASP.NET Core 程序。

## 2. 部署前準備

準備以下資料，以下範例請全部換成實際值：

| 變數 | 範例 | 用途 |
|---|---|---|
| `DOMAIN` | `time.example.com` | 已建立 A/AAAA 記錄，指向 EC2 Elastic IP |
| `ADMIN_CIDR` | `203.0.113.10/32` | 可管理主機的固定公網 IP |
| `APP_USER` | `ltm` | Linux 非登入服務帳號 |
| `DB_NAME` | `lyubishchev_time_management` | MySQL 資料庫 |
| `DB_USER` | `ltm_app` | 僅限此資料庫的 MySQL 帳號 |
| `S3_BUCKET` | `s3://my-ltm-backups` | 私有備份 bucket |
| `ADMIN_EMAIL` | `admin@example.com` | Let's Encrypt 憑證到期通知信箱 |

請為 EC2 配置 Elastic IP，否則重新啟動後公網 IP 改變會使 DNS 與 TLS 憑證失效。啟動前，讓 DNS 記錄先解析到 Elastic IP；Let’s Encrypt 的 HTTP-01 驗證需要從網際網路連到 80。

## 3. AWS 設定

1. 建立 Ubuntu Server 24.04 LTS EC2（建議至少 2 GiB RAM；小型個人服務可由 `t3.small` 起步），使用 gp3 EBS，並設定足以容納資料與備份暫存的磁碟容量。
2. 指派 Elastic IP、建立上述網域的 A 記錄；若啟用 IPv6，也建立 AAAA 記錄且下列防火牆規則需同時配置 IPv6。
3. 建立並套用 security group。入站規則只能有：

| 協定／埠 | 來源 | 用途 |
|---|---|---|
| TCP 22 | `ADMIN_CIDR` 或 EC2 Instance Connect Endpoint 的 security group | SSH 管理 |
| TCP 80 | `0.0.0.0/0`、`::/0` | HTTP 轉 HTTPS 與憑證更新 |
| TCP 443 | `0.0.0.0/0`、`::/0` | 網站 HTTPS |

**不要**建立 TCP 3306、5000、5180、7036、8080 或 8081 的公網入站規則。AWS security group 與主機防火牆都要遵守這個限制。出站可先保留預設全開；若日後收斂，至少要允許 DNS、HTTP/HTTPS（套件與 S3）及 NTP。

4. 建立私有 S3 bucket，啟用 Block Public Access、版本控制與預設加密。建立一個只允許 `s3:PutObject`、`s3:ListBucket`（以及還原時需要的 `s3:GetObject`）至該 bucket/prefix 的 IAM role，附加到 EC2；**不在主機寫 AWS access key**。

## 4. 首次登入與主機基線

```bash
ssh -i /path/to/key.pem ubuntu@EC2_ELASTIC_IP
sudo timedatectl set-timezone UTC
sudo apt-get update
sudo apt-get -y upgrade
sudo apt-get install -y ca-certificates curl unzip gnupg ufw nginx mysql-server certbot python3-certbot-nginx
curl -o /tmp/awscliv2.zip "https://awscli.amazonaws.com/awscli-exe-linux-x86_64.zip"
unzip -q /tmp/awscliv2.zip -d /tmp
sudo /tmp/aws/install
rm -rf /tmp/awscliv2.zip /tmp/aws
aws --version
sudo ufw default deny incoming
sudo ufw default allow outgoing
sudo ufw allow from ADMIN_CIDR to any port 22 proto tcp
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo adduser --system --group --home /srv/ltm --shell /usr/sbin/nologin ltm
sudo install -d -o ltm -g ltm -m 0750 /srv/ltm/app /srv/ltm/releases /srv/ltm/source
sudo install -d -o root -g ltm -m 0750 /etc/ltm
```

在另一個終端確認 SSH 仍可登入後，才結束原 SSH session。`ufw` 是第二層防護，不能取代 AWS security group。

## 5. 安裝 .NET 10

Ubuntu 24.04 的官方套件來源已提供 .NET 10。首次部署會在主機執行 EF migration，故安裝 SDK；日後若改成由 CI 產生 migration bundle，可降為 `aspnetcore-runtime-10.0`。

```bash
sudo apt-get update
sudo apt-get install -y dotnet-sdk-10.0
dotnet --info
```

確認輸出列出 .NET SDK 10.x。不要以 `launchSettings.json` 的本機 5180/7036 或 Docker 8080/8081 埠作為正式設定。

## 6. 設定 MySQL

MySQL 預設應只監聽 loopback。確認 `/etc/mysql/mysql.conf.d/mysqld.cnf` 的 `bind-address = 127.0.0.1`，再啟動與啟用服務：

```bash
sudo systemctl enable --now mysql
sudo mysql_secure_installation
sudo mysql
```

在 MySQL shell 執行（使用獨立密碼管理工具產生並保存 `DB_PASSWORD`；不要把它貼進 shell history、文件、Git 或 systemd unit）：

```sql
CREATE DATABASE lyubishchev_time_management
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_0900_ai_ci;
CREATE USER 'ltm_app'@'localhost' IDENTIFIED BY 'REPLACE_WITH_A_LONG_RANDOM_PASSWORD';
GRANT ALL PRIVILEGES ON lyubishchev_time_management.* TO 'ltm_app'@'localhost';
FLUSH PRIVILEGES;
EXIT;
```

驗證資料庫不對外監聽：

```bash
sudo ss -ltnp | grep ':3306'
mysql -u ltm_app -p -h 127.0.0.1 lyubishchev_time_management -e 'SELECT 1;'
```

第一個指令只能顯示 `127.0.0.1:3306`（或 `localhost`），不能是 `0.0.0.0:3306` 或公網 IP。

## 7. 修正反向代理必要程式設定（已完成）

`Program.cs` 已使用 `UseHttpsRedirection()`，原本尚未處理 Nginx 傳來的 `X-Forwarded-Proto`。若直接以 HTTP 反向代理到 Kestrel，應用程式會把每個請求誤判為 HTTP，造成 HTTPS redirect loop；rate limiter 也會只看到 Nginx 的 loopback IP。

**這項修改已經實作並合併**（`Program.cs`：`builder.Services.Configure<ForwardedHeadersOptions>(...)` + `app.UseForwardedHeaders()`，只信任 loopback 作為 forwarder），本機已手動驗證：帶 `X-Forwarded-For`/`X-Forwarded-Proto` 的請求會正確覆寫 `HttpContext.Connection.RemoteIpAddress`（登入失敗記錄的 `IpAddress` 確認變成標頭裡的位址，而不是 loopback），沒有 HTTPS redirect loop。以下保留原始程式碼供對照，不需要再手動加入：

```csharp
using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

// 在 builder.Services.AddControllersWithViews() 後加入。
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});

// 在 app.UseHttpsRedirection() 之前加入。
app.UseForwardedHeaders();
```

Nginx 與 Kestrel 同機且 Kestrel 只聆聽 loopback，因此只信任 `127.0.0.1` 與 `::1` 是正確的最小信任範圍。不得為了方便清空 `KnownProxies`／`KnownNetworks`，否則客戶端能偽造來源 IP 與 HTTPS 狀態。

每次發布前，在開發機（或 CI）至少執行（`WebFlow.Tests` 是第 15 項新增的第四個測試專案，包含全域例外處理與速率限制的整合測試）：

```powershell
dotnet test '.\Lyubishchev Time Management\Tests\AuthFlow.Tests\AuthFlow.Tests.csproj'
dotnet test '.\Lyubishchev Time Management\Tests\TimerFlow.Tests\TimerFlow.Tests.csproj'
dotnet test '.\Lyubishchev Time Management\Tests\TimeEntryFlow.Tests\TimeEntryFlow.Tests.csproj'
dotnet test '.\Lyubishchev Time Management\Tests\WebFlow.Tests\WebFlow.Tests.csproj'
dotnet publish '.\Lyubishchev Time Management\Lyubishchev Time Management.csproj' -c Release -o .\artifacts\ltm
```

## 8. 準備正式秘密設定

在 EC2 以 root 建立 `/etc/ltm/ltm.env`，內容使用 .NET 環境變數的雙底線階層格式：

```ini
ASPNETCORE_ENVIRONMENT=Production
ASPNETCORE_URLS=http://127.0.0.1:5000
ConnectionStrings__DefaultConnection=Server=127.0.0.1;Port=3306;Database=lyubishchev_time_management;User ID=ltm_app;Password=REPLACE_WITH_DB_PASSWORD;
Jwt__Issuer=LyubishchevTimeManagement
Jwt__Audience=LyubishchevTimeManagement
Jwt__SigningKey=REPLACE_WITH_A_32_PLUS_CHARACTER_HIGH_ENTROPY_SECRET
Jwt__ExpirationHours=8
```

```bash
sudoedit /etc/ltm/ltm.env
sudo chown root:ltm /etc/ltm/ltm.env
sudo chmod 0640 /etc/ltm/ltm.env
```

此檔案不可加入 repository、release artifact、shell script、日誌或 issue。若改採 AWS Secrets Manager，仍應在服務啟動時安全地載入到同等權限的設定來源；不要將 Secret ARN 以外的明文密碼交給一般使用者。

## 9. 發行應用程式與套用 migration

推薦在受控開發機或 CI 產出 Release artifact，再用 SSH/rsync 上傳，避免讓 production host 直接 `git pull`。以下假設本機已產生 `artifacts/ltm`：

```bash
rsync -av --delete -e 'ssh -i /path/to/key.pem' ./artifacts/ltm/ ubuntu@EC2_ELASTIC_IP:/tmp/ltm-release/
ssh -i /path/to/key.pem ubuntu@EC2_ELASTIC_IP
sudo rm -rf /srv/ltm/releases/RELEASE_ID
sudo mv /tmp/ltm-release /srv/ltm/releases/RELEASE_ID
sudo chown -R ltm:ltm /srv/ltm/releases/RELEASE_ID
sudo ln -sfn /srv/ltm/releases/RELEASE_ID /srv/ltm/app/current
```

第一次套 migration 前，先建立人工備份（第 12 節）。若選擇在主機套用 migration，須先把該已審核 commit 的原始碼（只需要 `.csproj`、程式碼與 `Data/Migrations/`，不含 `bin`/`obj`）部署到 `/srv/ltm/source/RELEASE_ID/`，並確保 `ltm` 使用者可讀取。不要在 production host 對未審核的 branch 執行 migration：

```bash
rsync -av --delete --exclude 'bin/' --exclude 'obj/' -e 'ssh -i /path/to/key.pem' \
  './Lyubishchev Time Management/' ubuntu@EC2_ELASTIC_IP:/tmp/ltm-source/
ssh -i /path/to/key.pem ubuntu@EC2_ELASTIC_IP
sudo rm -rf /srv/ltm/source/RELEASE_ID
sudo mv /tmp/ltm-source /srv/ltm/source/RELEASE_ID
sudo chown -R ltm:ltm /srv/ltm/source/RELEASE_ID
```

在該工作副本（下例為 `/srv/ltm/source/RELEASE_ID/Lyubishchev Time Management`）安裝 EF CLI 並套用 migration。`dotnet tool install --tool-path` 產生的是可直接執行的 shim（原生執行檔），**不要**再用 `dotnet` 呼叫它，直接呼叫 shim 本身即可：

```bash
sudo dotnet tool install --tool-path /usr/local/lib/ltm-tools dotnet-ef --version 10.*
sudo systemd-run --wait --collect --unit=ltm-migrate-RELEASE_ID \
  --property=EnvironmentFile=/etc/ltm/ltm.env \
  --property=WorkingDirectory='/srv/ltm/source/RELEASE_ID/Lyubishchev Time Management' \
  --property=User=ltm --property=Group=ltm \
  /usr/local/lib/ltm-tools/dotnet-ef database update --configuration Release
```

`EnvironmentFile` 由 systemd 讀取，因此不會把含分號的連線字串交給 shell 解析，也不會把秘密放進命令列。Migration 是 schema 變更，必須在啟動新版本前執行、確認成功，再切換服務。不要在多個 EC2 上同時執行此命令；目前架構只有一台主機。若改由 CI 執行，請用 CI 的 protected secret 注入相同設定，且避免在輸出中列印它們。

## 10. 建立 systemd 服務

建立 `/etc/systemd/system/ltm.service`：

```ini
[Unit]
Description=Lyubishchev Time Management ASP.NET Core application
After=network-online.target mysql.service
Wants=network-online.target

[Service]
Type=exec
User=ltm
Group=ltm
WorkingDirectory=/srv/ltm/app/current
EnvironmentFile=/etc/ltm/ltm.env
ExecStart=/usr/bin/dotnet /srv/ltm/app/current/Lyubishchev\ Time\ Management.dll
Restart=on-failure
RestartSec=5
TimeoutStopSec=30
NoNewPrivileges=true
PrivateTmp=true
ProtectSystem=full
ProtectHome=true
ReadWritePaths=/srv/ltm

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ltm
sudo systemctl status ltm --no-pager
curl -I http://127.0.0.1:5000/
sudo journalctl -u ltm -n 100 --no-pager
```

Kestrel 必須只顯示 `127.0.0.1:5000`。若啟動失敗，優先檢查 `/etc/ltm/ltm.env` 的檔案權限、資料庫連線、JWT signing key 與檔名空格是否已正確跳脫；日誌不可包含連線字串或 JWT。

## 11. 設定 Nginx 與 HTTPS

先建立 HTTP 設定 `/etc/nginx/sites-available/ltm`（將 `time.example.com` 換為真實網域）：

```nginx
server {
    listen 80;
    listen [::]:80;
    server_name time.example.com;

    location / {
        proxy_pass http://127.0.0.1:5000;
        proxy_http_version 1.1;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

```bash
sudo ln -s /etc/nginx/sites-available/ltm /etc/nginx/sites-enabled/ltm
sudo rm -f /etc/nginx/sites-enabled/default
sudo nginx -t
sudo systemctl reload nginx
curl -I http://time.example.com/
sudo certbot --nginx -d time.example.com --redirect --email ADMIN_EMAIL --agree-tos --no-eff-email
sudo systemctl enable --now certbot.timer
sudo certbot renew --dry-run
```

Certbot 會將 port 80 server block 改為永久轉址到 HTTPS，並新增 443 TLS 設定。驗證：

```bash
curl -I http://time.example.com/
curl -I https://time.example.com/
sudo nginx -t
sudo ss -ltnp | grep -E ':(80|443|5000|3306)'
```

HTTP 應回 `301` 或 `308` 並指向 HTTPS；HTTPS 應回 `200` 或登入頁的正常重新導向；5000 與 3306 不可由公網到達。

## 12. 每日 MySQL 備份至 S3

建立 `/usr/local/sbin/ltm-backup`，root 擁有、不可讓一般使用者修改：

```bash
#!/usr/bin/env bash
set -euo pipefail
umask 077

backup_dir=/var/backups/ltm
timestamp=$(date -u +%Y%m%dT%H%M%SZ)
file="$backup_dir/lyubishchev_time_management-$timestamp.sql.gz"
install -d -m 0700 "$backup_dir"

mysqldump --single-transaction --routines --events --databases lyubishchev_time_management \
  | gzip -9 > "$file"
aws s3 cp "$file" "s3://my-ltm-backups/mysql/" --only-show-errors
find "$backup_dir" -type f -name '*.sql.gz' -mtime +7 -delete
```

```bash
sudo chmod 0700 /usr/local/sbin/ltm-backup
sudo /usr/local/sbin/ltm-backup
sudo ls -lh /var/backups/ltm
```

若 `mysqldump` 需要專用帳號，將其憑證存入 root-only 的 `/root/.my.cnf`（`chmod 0600`），而不是寫在腳本或命令列。建立 `/etc/systemd/system/ltm-backup.service`：

```ini
[Unit]
Description=Back up Lyubishchev Time Management MySQL database to S3
After=mysql.service network-online.target
Wants=network-online.target

[Service]
Type=oneshot
ExecStart=/usr/local/sbin/ltm-backup
```

以及 `/etc/systemd/system/ltm-backup.timer`：

```ini
[Unit]
Description=Run LTM database backup every day

[Timer]
OnCalendar=*-*-* 02:15:00 UTC
Persistent=true

[Install]
WantedBy=timers.target
```

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now ltm-backup.timer
systemctl list-timers ltm-backup.timer
```

至少每季做一次還原演練至**不同名稱的空白資料庫**，確認 archive 真能使用：

```bash
aws s3 cp s3://my-ltm-backups/mysql/BACKUP_FILE.sql.gz /tmp/restore.sql.gz
mysql -u root -p -e 'CREATE DATABASE ltm_restore;'
gzip -dc /tmp/restore.sql.gz | mysql -u root -p ltm_restore
```

實際還原請在隔離環境完成；不要把 production restore 覆蓋到仍在服務的正式資料庫。

## 13. 發布、回滾與日常維運

每次發布順序：

1. 本機／CI 執行所有四個 test project（`AuthFlow.Tests`/`TimerFlow.Tests`/`TimeEntryFlow.Tests`/`WebFlow.Tests`），並產出 `dotnet publish -c Release` artifact。
2. 先執行手動 MySQL 備份，將 artifact 上傳到新 `RELEASE_ID` 目錄。
3. 審閱 migration；套用成功後才以 `ln -sfn` 切換 `/srv/ltm/app/current`。
4. `sudo systemctl restart ltm`，驗證第 14 節；保留上一個 release 目錄直到新版本穩定。

若應用程式版本失敗且 migration 可向前相容，可把 symlink 指回上一個 artifact，再重啟：

```bash
sudo ln -sfn /srv/ltm/releases/PREVIOUS_RELEASE_ID /srv/ltm/app/current
sudo systemctl restart ltm
sudo systemctl status ltm --no-pager
```

**不要**未經備份就執行 migration down 或手動刪表。資料庫回滾必須依 migration 的可逆性與還原演練決定；程式回滾不等於資料庫 schema 回滾。

日常檢查：

```bash
sudo systemctl status ltm nginx mysql ltm-backup.timer --no-pager
sudo journalctl -u ltm --since '24 hours ago' --no-pager
df -h
aws s3 ls s3://my-ltm-backups/mysql/ | tail
sudo apt-get update && apt list --upgradable
```

定期更新 Ubuntu、.NET、Nginx 與 MySQL；安排維護窗口並於升級前備份。系統有單一 EC2 的單點故障風險，S3 外部備份是 V1 的最低要求，不等同高可用性。

## 14. 上線驗收清單

- [ ] DNS 的 A/AAAA 記錄指向 EC2，HTTPS 憑證有效，HTTP 會轉 HTTPS。
- [ ] Security group 與 UFW 沒有公網 SSH、MySQL、Kestrel 或開發埠；SSH 僅限管理來源。
- [ ] `ltm`、`nginx`、`mysql` 與 `ltm-backup.timer` 均為 enabled/running。
- [ ] `ss` 顯示 Kestrel 為 `127.0.0.1:5000`、MySQL 為 loopback；外部僅能存取 80/443。
- [ ] 以 HTTPS 註冊、登入、登出；瀏覽器中的 `ltm_auth` 為 Secure、HttpOnly、SameSite=Strict，且登入後可進入 `/Dashboard`。
- [ ] Nginx 代理後沒有 HTTPS redirect loop，登入/Register rate limiting 會依真實用戶端 IP 計算。
- [ ] migration 已套用，基本資料流程可建立計時器、停止計時器、建立／編輯 TimeEntry。
- [ ] 手動執行一次備份成功、S3 可看到新 archive，並已排定隔離環境的還原演練。
- [ ] 未把 DB 密碼、JWT signing key、cookie、access key 或完整連線字串寫進 Git、systemd unit、Nginx 設定或日誌。

## 15. 參考資料

- [.NET on Ubuntu 24.04 installation](https://learn.microsoft.com/en-us/dotnet/core/install/linux-ubuntu-install)
- [AWS EC2 security groups](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/creating-security-group.html)
- [AWS security-group rules for web servers](https://docs.aws.amazon.com/AWSEC2/latest/UserGuide/security-group-rules-reference.html)
- [AWS CLI installation](https://docs.aws.amazon.com/cli/latest/userguide/getting-started-install.html)
- [Certbot documentation](https://eff-certbot.readthedocs.io/)
