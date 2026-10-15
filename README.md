# drAsistent – Generarea graficului de lucru pe secție

Aplicație web (ASP.NET Core 10 + Blazor Server) pentru generarea automată a graficului lunar
pentru asistente, infirmiere și îngrijitoare. Datele se salvează în SQL Server.

## Structură

| Proiect | Rol |
|---|---|
| `src/DrAsistent.Core` | Reguli: normă, concedii, ore weekend/sărbători, validare, sărbători legale RO |
| `src/DrAsistent.Solver` | Generarea graficului cu Google OR-Tools (CP-SAT) |
| `src/DrAsistent.Data` | EF Core + SQL Server, migrări |
| `src/DrAsistent.Export` | Export Excel (ClosedXML) și PDF (QuestPDF) |
| `src/DrAsistent.Web` | Interfața web, autentificare, endpoint-uri de export |
| `tests/DrAsistent.Tests` | Teste pentru calcule și generator |

## Reguli implementate

**Obligatorii (HARD)**
- o singură tură pe zi; Z = 07–19, N = 19–07, Z8 = 07–15
- după N urmează cel puțin o zi liberă (și la trecerea dintre luni)
- concediile CO/CM: doar zilele lucrătoare se scad din normă (8h/zi); weekendurile și sărbătorile din concediu sunt L
- fără N în ajunul unei zile de concediu
- îngrijitoarele nu fac N
- acoperirea minimă (configurabilă în *Setări*); implicit:
  2 asistente zi, 1 asistentă noapte, 2 infirmiere/îngrijitoare zi, 1 infirmieră noapte

**Recomandate (SOFT), în ordinea priorității**
- ore lucrate = țintă (normă − concedii − sold reportat); diferența se reportează în luna următoare
- acoperirea dorită (ex. a doua asistentă de noapte)
- echitate ore weekend/sărbători (±2h) pe categorie și între categoriile interschimbabile
  (infirmiere + îngrijitoare), raportat la normă pentru cei cu concediu
- cât mai puține ture Z8, personalul în plus pus de preferință în zilele lucrătoare
- tiparul Z → N → L → L

Celulele modificate manual în grafic sunt fixate și se păstrează la regenerare.

## Instalare pe server (Arcane + SQL Server existent)

### 1. Conexiunea la SQL Server

Aplicația primește tot connection string-ul prin variabila `DB_CONNECTION`. Valoarea pentru `Server=` depinde de unde se află SQL Server:

- **Varianta recomandată:** `SQLSERVER1` rulează pe același host și are portul publicat (`docker port SQLSERVER1` afișează `1433/tcp -> 0.0.0.0:1433`).
  Folosiți `Server=host.docker.internal,1433`; compose-ul mapează deja acest nume la host.
- **Alt server:** folosiți IP-ul sau numele lui, de exemplu `Server=192.168.1.50,1433`.
- **Portul nu e publicat:** puneți ambele containere într-o rețea comună
  (`docker network create sql-net` și `docker network connect sql-net SQLSERVER1`), apoi adăugați rețeaua în compose
  și folosiți `Server=SQLSERVER1,1433`.

Nu folosiți IP-ul intern al containerului (172.17.x.x), pentru că se poate schimba la repornire.

### 2. Baza de date și utilizatorul aplicației

Rulați o singură dată, ca `sa` (în locul parolei puneți una puternică):

```sql
CREATE DATABASE DrAsistent;
GO
CREATE LOGIN drasistent_app WITH PASSWORD = '<parola-puternica>';
GO
USE DrAsistent;
CREATE USER drasistent_app FOR LOGIN drasistent_app;
ALTER ROLE db_owner ADD MEMBER drasistent_app;
GO
```

Tabelele se creează și se actualizează automat la pornirea aplicației.

### 3. Imaginea Docker

Varianta recomandată: urcați codul pe GitHub. Workflow-ul `.github/workflows/docker.yml` rulează testele și
publică imaginea în `ghcr.io/<utilizator>/drasistent:latest` la fiecare push pe `main`.
Dacă pachetul GHCR rămâne privat, adăugați registry-ul cu un token în Arcane.

Ca alternativă, imaginea se poate construi direct pe server:

```bash
docker build -t drasistent:latest .
```

### 4. Proiectul în Arcane

1. Creați un proiect nou și copiați conținutul din `docker-compose.yml`.
2. În `.env`-ul proiectului completați valorile din `.env.example`: `IMAGE`, `DB_CONNECTION`, `ADMIN_PASSWORD` etc.
3. Porniți proiectul. La prima pornire se creează contul `ADMIN_USER` cu parola `ADMIN_PASSWORD`.
   Parola se poate schimba apoi din pagina *Utilizatori*.

### 5. HTTPS

Aplicația ascultă HTTP pe portul 8080. Puneți-o în spatele unui reverse proxy cu certificat
(Nginx Proxy Manager, Traefik, Caddy). Antetele `X-Forwarded-*` sunt acceptate.
Nu expuneți aplicația direct pe internet fără HTTPS, pentru că conține date despre concedii medicale.

## Dezvoltare locală

```bash
# cu SQL Server LocalDB
set ConnectionStrings__Default=Server=(localdb)\MSSQLLocalDB;Database=DrAsistentDev;Trusted_Connection=True;TrustServerCertificate=True
set Admin__User=admin
set Admin__Password=Parola-Test123
dotnet run --project src/DrAsistent.Web

dotnet test
```

Migrare nouă după modificarea entităților:

```bash
dotnet ef migrations add <Nume> -p src/DrAsistent.Data -s src/DrAsistent.Data -o Migrations
```

## Backup

Datele sunt în baza `DrAsistent` de pe `SQLSERVER1`. Includeți-o în backup-ul existent al serverului SQL,
de exemplu cu un job zilnic `BACKUP DATABASE DrAsistent TO DISK = '/var/opt/mssql/backup/DrAsistent.bak'`.
