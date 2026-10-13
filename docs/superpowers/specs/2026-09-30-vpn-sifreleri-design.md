# VPN Şifreleri — tasarım

Tarih: 2026-09-30 · Durum: onaylandı

## Amaç

Müşterilerin VPN şifreleri OneNote'ta duruyor; her bağlantıda doğru notu bulup şifreyi aramak gerekiyor.
VPN Şifreleri, saatin yanındaki ikondan müşterinin şifresini tek tıkla panoya kopyalayan küçük bir Windows tepsi
uygulaması. Kayıtlar "kokpit" penceresinden eklenir, güncellenir ve silinir; menü değişikliği hemen gösterir.

Başarı ölçütü: sağ tık → müşteri adı → şifre panoda. OneNote açılmaz, hiçbir şifre sorulmaz.

## Netleşen kararlar

| Konu | Karar |
|---|---|
| Kayıt | Yalnızca müşteri adı + şifre. Bir müşterinin iki VPN'i iki ayrı kayıt olur ("Köprü Test", "Köprü Canlı") |
| Koruma | Dosya Windows oturumuna bağlı şifrelenir (DPAPI, CurrentUser). Ana şifre **yok**: uygulama hiçbir zaman şifre sormaz, hiçbir hesaba bağlanmaz, internete çıkmaz |
| Sıralama | Türkçe alfabe (tr-TR), büyük/küçük harf duyarsız. Aynı isim kontrolü de aynı kuralla |
| Kokpit düzeni | Üstte ad + şifre kutuları ve Ekle/Güncelle, altta liste |
| Bildirim | Şifreyi göstermez: "Şifre kopyalandı" + müşteri adı (ekran paylaşımında güvenli) |
| Pano | Win+V geçmişine ve buluta gitmez; 30 sn sonra ve çıkışta, hâlâ panodaysa silinir |
| Windows ile başlat | Menüden elle açılır, kendiliğinden açılmaz |
| Dağıtım | Yalnızca bu bilgisayar. GitHub'a ve Store'a konmaz; git deposu yereldir |

## Mimari

Test Veri Üretici'nin temeli aynen kullanılır: Windows'la gelen .NET Framework `csc.exe` ile **C# 5** (`$""`, `?.`,
`=>` üyeler yok), kurulumsuz tek exe, WinForms `NotifyIcon` + klasik `ContextMenu`. Store'a özel parçalar (MSIX,
StartupTask, `AppPackage`) alınmaz. Proje: `C:\Users\Alperen\projects\VpnSifreleri`, namespace `VpnSifreleri`.

| Dosya | Görevi |
|---|---|
| `TrayApp.cs` | `Program.Main` (tek örnek, ikinci açılışta kokpit sinyali) ve `TrayContext` (ikon, menü, kopyalama, kokpiti açma, Windows ile başlat) |
| `PasswordStore.cs` | `Entry` (Name, Password), sıralı liste, ekleme/güncelleme/silme kuralları, DPAPI ile okuma/yazma |
| `TrayMenu.cs` | Kayıtlardan menü öğelerini üretir (`&` kaçışı, boş liste öğesi) |
| `SecureClipboard.cs` | Geçmişe ve buluta gitmeyen pano verisi; 30 sn sonra ve çıkışta temizleme |
| `CockpitForm.cs` | Kokpit penceresi; yalnızca arayüz, kurallar `PasswordStore`'da |
| `Toast.cs` | Test Veri Üretici'den aynen (yalnızca namespace değişir) |
| `TrayPin.cs` | Test Veri Üretici'den; paket kontrolü ve Store ipucu çıkarılır |
| `AutoStart.cs` | Yalnızca `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` kaydı (`VpnSifreleri`, `--autostart`) |
| `AppIcon.cs` | Yeşil kutu + beyaz kilit: tepsi ikonu ve exe'ye gömülen çok boyutlu .ico |
| `AssemblyInfo.cs` | "VPN Şifreleri", sürüm 1.0.0.0 |
| `Tests.cs` | Konsol test çalıştırıcısı |
| `build.ps1` | Testler → ikon → exe; çalışan kopyayı önce kapatır |

### Açılış ve tek örnek

- `Local\VpnSifreleri` mutex'i tek örneği garanti eder.
- İkinci açılış `AllowSetForegroundWindow(ASFW_ANY)` çağırır, `Local\VpnSifreleri.Kokpit` olayını tetikler ve kapanır;
  çalışan örnek kokpiti açar ya da öne getirir. `--autostart` ile gelen ikinci açılış hiçbir şey yapmadan kapanır.
- Elle açılışta "VPN Şifreleri çalışıyor / Saatin yanındaki kilit ikonuna sağ tıkla" bildirimi çıkar; hiç kayıt yoksa
  kokpit de kendiliğinden açılır. Oturum açılışında (`--autostart`) sessiz başlar.
- İkon ilk açılışta `TrayPin` ile saatin yanına yerleşir; kullanıcı sonradan gizlerse tercihine dokunulmaz.

## Saklama

- Dosya: `%APPDATA%\VpnSifreleri\sifreler.dat`.
- İçerik: `BinaryWriter` ile `"VPNS"` imzası, biçim sürümü (1), kayıt sayısı ve her kayıt için ad + şifre (UTF-8,
  uzunluk önekli; her karakter güvenle saklanır). Bu baytların tamamı
  `ProtectedData.Protect(veri, entropy, DataProtectionScope.CurrentUser)` ile şifrelenir; adlar da açıkta kalmaz.
  `entropy` uygulamaya özel sabit bir bayt dizisidir.
- Yazma: önce `sifreler.dat.tmp`, sonra `File.Replace` ile asıl dosyanın yerine; önceki sürüm `sifreler.dat.bak` olur.
  Dosya ilk kez yazılırken `File.Move`. Klasör yoksa oluşturulur.
- Değişiklik önce listenin kopyasında yapılır; kopya kaydedilirse bellekteki liste onunla değişir. Kaydedilemezse
  bellekte de hiçbir şey değişmez.
- Okuma: dosya yoksa boş liste. Şifre çözülemez ya da biçim bozuksa dosya silinmez,
  `sifreler.dat.okunamadi-yyyyMMdd-HHmmss` adıyla saklanır; uygulama bir uyarı gösterip boş listeyle açılır.
  Dosya kenara alınamazsa uygulama ona hiç dokunmadan hata gösterip kapanır (üzerine asla yazılmaz).
- Bilinen sınır: DPAPI anahtarı Windows kullanıcı profiline bağlıdır. Dosya başka bir bilgisayarda ya da Windows yeniden
  kurulduktan sonra açılmaz. v1'de dışa aktarma yok; OneNote yedek olarak kalır.

## Kayıt kuralları (`PasswordStore`)

- Ad ve şifrenin baştaki/sondaki boşlukları silinir.
- Boş ad → "Müşteri adı boş olamaz"; boş şifre → "Şifre boş olamaz".
- Karşılaştırma ve sıralama `StringComparer.Create(tr-TR, ignoreCase: true)`: `KÖPRÜ` = `köprü`,
  `İSTANBUL` = `istanbul`.
- `Find(ad)`: aynı kurala göre kaydı bulur.
- `Add(ad, şifre)`: aynı ad varsa `NameExists` döner, hiçbir şey değişmez.
- `Update(eskiAd, yeniAd, şifre)`: yeni ad başka bir kayıtta varsa `NameExists`; aynı kaydın yalnızca harf büyüklüğünü
  değiştirmek serbest.
- `Remove(ad)`.
- `Entries` her zaman sıralıdır. Her başarılı değişiklikten sonra `Changed` olayı tetiklenir; menü yeniden kurulur.
- Doğrulama hataları sonuç değeri olarak döner; kaydetme hataları (`IOException`, `UnauthorizedAccessException`,
  `CryptographicException`) istisna olarak yükselir ve bellekteki liste değişmemiş olur.

## Tepsi menüsü

```
Açelya
Köprü
Üzüm
──────────────────
Şifreleri yönet…      (kalın: ikona çift tıklamanın yaptığı iş)
Windows ile başlat
Çıkış
```

- Kayıt yoksa en üstte tıklanamayan "(Henüz kayıt yok)" öğesi durur.
- Adlardaki `&` menüde kısayol harfi sayılmasın diye `&&` yazılır.
- Kayda tıklama → `SecureClipboard.Copy(şifre)` → bildirim: üst satır "Şifre kopyalandı", alt satır müşteri adı.
  Pano başka bir uygulamada kilitliyse "Panoya kopyalanamadı, tekrar dene" bildirimi.
- Menü yalnızca sağ tıkla açılır; ikona çift tıklamak kokpiti açar, tek sol tık bir şey yapmaz.

## Pano (`SecureClipboard`)

- Panoya konan `DataObject`: `UnicodeText` + `ExcludeClipboardContentFromMonitorProcessing` +
  `CanIncludeInClipboardHistory` = 0 (DWORD) + `CanUploadToCloudClipboard` = 0 (DWORD).
- `Clipboard.SetDataObject(veri, true, 10, 50)`: başka bir uygulama panoyu tutuyorsa kısa aralıklarla yeniden dener.
- 30 sn sonra panodaki metin hâlâ kopyalanan şifreyse pano temizlenir; bu arada başka bir şey kopyalandıysa dokunulmaz.
  Yeni bir kopyalama süreyi yeniden başlatır. Çıkışta aynı kontrol yapılır. Temizleme sırasında pano kilitliyse
  sessizce geçilir.

## Kokpit (`CockpitForm`)

```
┌ VPN Şifreleri ───────────────────────────── x ┐
│ Müşteri            Şifre                      │
│ [Köprü        ]   [••••••••    ] [Ekle]      │
│                     [ ] Göster                │
│───────────────────────────────────────────────│
│ Müşteri            Şifre                      │
│ Açelya            ••••••••                   │
│ Köprü             ••••••••                   │
│ Üzüm              ••••••••                   │
│                                               │
│ ✓ Köprü eklendi                        [Sil] │
└───────────────────────────────────────────────┘
```

- Tek pencere: açıksa öne gelir. Kapatınca uygulama tepside çalışmaya devam eder. Her açılışta Ekle modunda ve
  şifreler gizli başlar. Görev çubuğunda görünür, boyutu değiştirilebilir, ekran ölçeğine (DPI) göre büyür.
- Enter = Ekle/Güncelle. Ekledikten sonra kutular temizlenir, imleç ad kutusuna döner, yeni kayıt listede görünür hale
  kaydırılır (seçilmez), durum satırında "✓ Köprü eklendi" yazar.
- Ad zaten varsa: "Köprü zaten var. Şifresi güncellensin mi?" (Evet/Hayır). Evet → mevcut kaydın şifresi güncellenir,
  adı olduğu gibi kalır.
- Listeden seçme → Güncelle modu: ad ve şifre kutulara gelir, düğme "Güncelle" olur, yanında "Vazgeç" çıkar.
  Güncelledikten sonra Ekle moduna döner ("✓ Köprü güncellendi"). Güncelle modunda başka bir kayıt seçilirse kutular
  yeni kayıtla dolar; kaydedilmemiş değişiklik bırakılır.
- Vazgeç, Esc ya da listenin boş yerine tıklama → Ekle modu (kutular temizlenir). Ekle modunda Esc pencereyi kapatır.
- Sil düğmesi ya da listede Delete tuşu → "Köprü silinsin mi?" (Evet/Hayır). Kayıt seçili değilken Sil pasiftir.
- "Göster": hem şifre kutusundaki hem listedeki şifreleri açık gösterir. Gizliyken listede her şifre "••••••••" görünür
  (uzunluk belli olmaz).
- Doğrulama hataları durum satırında kırmızı yazılır; kaydetme hatası "Kaydedilemedi: …" uyarı penceresiyle gösterilir
  ve kutulardaki metin korunur.

## Hata durumları

| Durum | Davranış |
|---|---|
| Şifre dosyası okunamadı | Uyarı penceresi; dosya yeniden adlandırılıp saklanır; boş listeyle devam |
| Okunamayan dosya kenara alınamadı | Hata penceresi; dosyaya dokunulmadan uygulama kapanır |
| Kaydedilemedi | "Kaydedilemedi: …" uyarısı; bellekte değişiklik yok; kutulardaki metin kalır |
| Pano meşgul (kopyalarken) | "Panoya kopyalanamadı, tekrar dene" bildirimi |
| Pano meşgul (temizlerken) | Sessizce geçilir |
| Windows ile başlat yazılamadı | "Ayar kaydedilemedi" bildirimi; menüdeki işaret gerçek durumu gösterir |

## Test

`build.ps1` önce `Tests.exe`'yi derleyip çalıştırır; bir test bile başarısızsa exe üretilmez. Testler geçici bir klasörde
çalışır; gerçek `sifreler.dat`'a ve panoya dokunmaz.

- Kaydet → aç: Türkçe karakter, `&`, tırnak, sekme ve emoji içeren adlar/şifreler aynen geri gelir.
- Dosya baytlarında adların ve şifrelerin UTF-8 ve UTF-16 halleri geçmez.
- Farklı `entropy` ile yazılmış (başka bilgisayarı taklit eden), rastgele baytlı ve imzası yanlış dosya → "okunamadı"
  yolu: dosya yeniden adlandırılır, baytları korunur, liste boş gelir.
- İkinci kayıttan sonra `.bak` oluşur; `.tmp` kalmaz.
- Sıralama: `açelya < Cam < Çelik < Işık < İnci < Uğur < Üzüm`.
- Aynı isim: `KÖPRÜ` = `köprü`; yeniden adlandırma çakışması reddedilir; yalnız harf büyüklüğü değişikliği serbest.
- Boş ad/şifre reddedilir; boşluklar kırpılır; kaydedilemeyen bir hedefte istisna yükselir ve bellek değişmez.
- Menü: `B&B` → `B&&B`; sıra kayıtlarla aynı; boş liste → tıklanamayan "(Henüz kayıt yok)"; öğeye tıklama doğru kaydı
  kopyalama fonksiyonuna verir.
- Pano verisi: `DataObject` üç biçimi ve DWORD 0 değerlerini taşır (panoya konmadan kontrol edilir); "temizlensin mi"
  kararı yalnızca metin aynıysa evet der.
- `--autostart` algılama; Windows ile başlat durumu Run kaydını yansıtır.
- `TrayPin.ResolveShellPath` testleri Test Veri Üretici'den taşınır.

Elle doğrulama: menü sırası ve kopyalama, bildirimde şifrenin görünmemesi, Win+V'de görünmemesi, 30 sn sonra temizleme,
kokpit akışları (ekle, aynı ad, güncelle, yeniden adlandır, sil, göster, Esc), ikinci açılışta kokpitin öne gelmesi,
ikonun saatin yanına yerleşmesi.

## Kapsam dışı (v1)

Dışa/içe aktarma, kullanıcı adı alanı, arama kutusu, bilgisayarlar arası eşitleme, GitHub/Store dağıtımı.
