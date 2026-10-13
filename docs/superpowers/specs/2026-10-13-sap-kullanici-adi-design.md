# Sürüm 1.1.0: SAP kullanıcı adı, güncelleme bildirimi, GitHub — tasarım

Tarih: 2026-10-13 · Durum: onaylandı (inceleme sonrası eklemeler en sonda) · Önceki tasarım: `2026-09-30-vpn-sifreleri-design.md`

## Amaç

1. **SAP girişi:** SAP'de kullanıcı adı ve şifre iki satır olarak (üstte kullanıcı adı, altta şifre) yapıştırılınca SAP
   ikisini doğru kutulara dağıtıyor. Uygulama bu iki satırı tek tıkla panoya koyabilmeli.
2. **Arkadaşlar:** Uygulama exe olarak arkadaşlara da verildi. Yeni sürümleri alabilmeleri için uygulama herkese açık bir
   GitHub deposunda yayımlanır ve yeni sürüm çıkınca uygulama haber verir.
3. Önceki incelemeden ertelenen dört küçük hata düzeltilir.

Başarı ölçütü: "Kartal SAP PRD"ye tıklayınca pano `kullanici1` + satır sonu + `test123` olur; VPN kayıtları bugünkü gibi
yalnızca şifreyi kopyalar; mevcut kayıtların hiçbiri kaybolmaz; 1.1.0'ı kuran biri ileride 1.2.0 çıkınca bunu
uygulamadan öğrenir.

## Netleşen kararlar

| Konu | Karar |
|---|---|
| Menü | Tek alfabetik liste; kayıt adlarını kullanıcı verir ("Kartal SAP PRD", "Kartal VPN") |
| Kayıt | Her kayda isteğe bağlı **kullanıcı adı**; boşsa davranış bugünkü gibi |
| Pano metni | Kullanıcı adı varsa `kullanıcı` + `\r\n` + `şifre`, yoksa yalnızca şifre |
| Bildirim | "Kullanıcı adı ve şifre kopyalandı" / "Şifre kopyalandı" + kayıt adı; kullanıcı adı ve şifre görünmez |
| Pano güvenliği | Aynı: Win+V geçmişine ve buluta gitmez, 30 sn sonra ve çıkışta (değişmemişse) silinir |
| Dağıtım | Herkese açık GitHub deposu `alperennkls/VpnSifreleri`, MIT lisansı, sürümler Releases sayfasında exe olarak |
| Güncelleme | Uygulama yalnızca GitHub'a son sürüm numarasını sorar; yeni sürüm varsa haber verir, indirme sayfasını açar. Kendini güncellemez |
| İnternet | Önceki tasarımda uygulama hiç internete çıkmıyordu; artık **yalnızca** bu sürüm sorusu için çıkar, hiçbir veri göndermez |
| Sürüm | 1.1.0 (etiket `v1.1.0`) |
| Kapsam dışı | Yalnız kullanıcı adını ayrıca kopyalamak, SAP Logon'a otomatik giriş, kendini güncelleme, Microsoft Store |

## Dosya biçimi (sürüm 2)

- İmza `"VPNS"`, biçim sürümü **2**, kayıt sayısı, her kayıt için ad + kullanıcı adı + şifre (UTF-8, uzunluk önekli).
  Tamamı yine DPAPI (CurrentUser, aynı entropy) ile şifreli.
- Okuma: sürüm 1 (ad + şifre, kullanıcı adı boş) ve sürüm 2 okunur. Yazma her zaman sürüm 2.
- Geçiş: 1.1.0 kullanıcının sürüm 1 dosyasını açar; ilk kayıtta dosya sürüm 2 olur, sürüm 1 hali `.bak`'ta kalır
  (1.1.0 onu da okur).
- **Daha yeni biçim** (imza doğru, sürüm > 2): dosya "okunamadı" diye kenara **alınmaz**; açılış dosyaya dokunmadan
  "daha yeni bir sürümle kaydedilmiş, uygulamayı güncelle" diye durur (mevcut "Şifre dosyası açılamadı. Dosyaya
  dokunulmadı" penceresi). Sürüm ≤ 0 ya da imza yanlış: bugünkü gibi okunamadı sayılır.
- 1.0.0 exe'si sürüm 2 dosyasını okuyamaz (kenara alır). Güncelleme anlatımı bu yüzden "eski exe'yi menüden kapat, yenisini
  eskisinin üzerine koy, aç" der; güncellemeden sonra eski exe açılmamalı.

## Kayıt kuralları (`PasswordStore`, `Entry`)

- `Entry`'ye `Username` (boş olabilir) ve panoya gidecek metni veren `ClipboardText` eklenir.
- `Add(ad, kullanıcı, şifre)` ve `Update(eskiAd, yeniAd, kullanıcı, şifre)`; kullanıcı adı da kırpılır, boş olabilir.
  Kullanıcı adsız eski çağrılar (`Add(ad, şifre)`, `Update(eskiAd, yeniAd, şifre)`) kullanıcı adını boş yazar.
- Kırpma artık boşluk ve satır sonlarına ek olarak baştaki/sondaki görünmez sıfır genişlikli karakterleri de siler
  (U+200B, U+200C, U+200D, U+2060, U+FEFF) — ad, kullanıcı adı ve şifrede. *(Ertelenen bulgu 8)*
- İçinde satır sonu kalan ad, kullanıcı adı ya da şifre (iki satırın birden tek kutuya yapıştırılması) kaydedilmez:
  "Ad, kullanıcı adı ve şifre tek satır olmalı".

## Kokpit

```
Müşteri            Kullanıcı adı      Şifre
[Kartal SAP PRD]   [kullanici1   ]    [•••••••]  [Ekle]
                                       [ ] Göster
────────────────────────────────────────────────────────
Müşteri            Kullanıcı adı      Şifre
Akasya VPN                            ••••••••
Kartal SAP PRD     kullanici1         ••••••••
```

- Pencere 620×440'a (mantıksal) genişler; Şifre sütunu kalan genişliği doldurur.
- Seçince üç kutu dolar; güncelleme kutulardaki üç değeri yazar.
- Var olan adla ekleme: kutulardaki kullanıcı adı kayıttakiyle aynıysa (ikisi de boş dahil) soru bugünkü gibi
  "‹ad› zaten var. Şifresi güncellensin mi?", farklıysa "‹ad› zaten var. Kullanıcı adı ve şifresi güncellensin mi?".
  Evet → kayıt kutulardaki kullanıcı adı ve şifreyle güncellenir, ad olduğu gibi kalır.
- Durum satırında `&` kısayol harfi sayılmaz (`UseMnemonic = false`): "B&B" eklenince "✓ B&B eklendi". *(Ertelenen bulgu 1)*
- Kokpit zaten açıkken tepsiden (menü, çift tık, exe'yi yeniden açma) öne getirilince "Göster" kapanır, şifreler
  gizlenir. *(Ertelenen bulgu 3)*

## Pano

- Kopyalanan metin `Entry.ClipboardText`.
- Panoya koyma iki adımdır (koyma + kalıcılaştırma). Hata gelse bile metin panoya ulaşmışsa kopyalama başarılı sayılır,
  30 sn temizleme kurulur ve "kopyalandı" bildirimi çıkar; ulaşmamışsa bugünkü gibi "Panoya kopyalanamadı".
  *(Ertelenen bulgu 2)*

## Güncelleme bildirimi

- Açılıştan 10 sn sonra ve uygulama açık kaldıkça 24 saatte bir, arka planda
  `https://api.github.com/repos/alperennkls/VpnSifreleri/releases/latest` istenir (TLS 1.2, `User-Agent:
  VpnSifreleri/<sürüm>`, sistem proxy'si ve Windows kimliğiyle). Yanıttan yalnızca `tag_name` (`v1.2.0`) okunur.
- Etiket çalışan sürümden büyükse (3 haneli karşılaştırma, baştaki `v` atılır):
  - Menüde ayraçtan sonra, "Şifreleri yönet…"in hemen üstünde **"Yeni sürüm var (1.2.0)…"** öğesi çıkar; tıklayınca
    `https://github.com/alperennkls/VpnSifreleri/releases/latest` tarayıcıda açılır.
  - O sürüm için bir kez bildirim: "Yeni sürüm var: 1.2.0" / "Menüden indirebilirsin".
- Ağ yok, GitHub yanıt vermiyor, sınır aşıldı, beklenmeyen yanıt: sessizce geçilir, bir sonraki denemede tekrar sorulur.
- Kullanıcı verisi, kayıt adları ya da başka hiçbir bilgi gönderilmez; yalnızca bu tek GET isteği.
- Not: 1.0.0'da bu özellik yok; arkadaşlar 1.1.0'ı bir kez elle indirir, sonrasını uygulama haber verir.

## GitHub'da yayın

- Depo: `https://github.com/alperennkls/VpnSifreleri` (herkese açık), commit kimliği önceki projedeki gibi kullanıcının
  git ayarı. `gh` yok: GitHub API'si Git Credential Manager'daki `alperennkls` belirteciyle Git Bash'ten çağrılır.
- **Müşteri adları yayımlanmaz:** testlerdeki ve belgelerdeki gerçek firma/müşteri adları ve kişisel örnekler
  (ör. önceki örnek kullanıcı adı) uydurma adlarla değiştirilir. Yerel geçmişte bu adlar geçtiği için depo **temiz,
  tek commit'lik bir geçmişle** başlatılır; eski ayrıntılı geçmiş yalnızca yerelde `yerel-gecmis` dalında kalır, hiç
  gönderilmez. Göndermeden önce bütün izlenen dosyalarda yasaklı ad listesiyle arama yapılır; tek eşleşme bile gönderimi
  durdurur.
- `README.md` (Türkçe): ne işe yarar, indir bağlantısı, ilk kurulum, güncelleme adımları (eski exe'yi menüden kapat →
  yenisini üzerine koy → aç), "Windows kişisel bilgisayarınızı korudu" uyarısı (imzasız exe), gizlilik (kayıtlar yalnızca
  bu bilgisayarda, Windows oturumuyla şifreli; uygulama yalnızca sürüm numarası için GitHub'a sorar), kaynaktan derleme.
- `LICENSE`: MIT.
- Releases: `v1.1.0` sürümü, `VpnSifreleri.exe` eklenmiş olarak.

## Test

Otomatik (build.ps1, geçici klasör; gerçek dosyaya, panoya ve internete dokunmaz):
- Sürüm 1 dosyası (eski biçimle yazılmış baytlar) okunur, kullanıcı adları boş gelir; kaydedince sürüm 2 olur, `.bak`
  sürüm 1 kalır ve okunur.
- Sürüm 2 kaydet-aç: kullanıcı adları (Türkçe karakter dahil) aynen geri gelir.
- Daha yeni biçim dosyası: açılış istisna verir, dosya baytları aynı kalır, kenara alınmaz.
- `ClipboardText`: kullanıcı adı varsa iki satır (`\r\n`), yoksa yalnız şifre; bildirim başlığı iki durumda doğru.
- Görünmez karakter kırpma: ad, kullanıcı adı ve şifrede.
- Pano: koyma hatası + metin panoda → başarılı ve temizleme kurulu; koyma hatası + metin yok → hata (sahte
  koy/oku/temizle işlevleriyle).
- Kokpit: kullanıcı adı kutusu ekle/güncelle/seç/temizle; iki soru metni; liste sütunları; `UseMnemonic` kapalı;
  "Göster"i kapatan çağrı.
- Güncelleme: GitHub yanıtından etiket okuma (gerçek yanıt örneği, `v` önekli/öneksiz, bozuk JSON, etiket yok) ve
  sürüm karşılaştırma (büyük/eşit/küçük, 2 ve 3 haneli).

Gerçek veriyle geçiş denemesi: kullanıcının `sifreler.dat` dosyasının **kopyası** geçici klasörde eski ve yeni kodla
açılır; kayıt sayısı ve ad/kullanıcı/şifre özetleri (SHA-256) karşılaştırılır, içerik yazdırılmaz. Yeni kodla kaydedip
yeniden açınca özetler aynı kalmalı.

Uçtan uca (gerçek exe): kullanıcının uygulaması menüden kapatılır, `%APPDATA%\VpnSifreleri` klasörünün adı geçici olarak
değiştirilir, sahte kayıtlarla denenir (SAP kaydında pano tam olarak `kullanici1\r\ntest123`, VPN kaydında yalnız şifre,
üç pano biçimi, bildirim, "Göster"in öne getirmede kapanması), sonra test klasörü silinir, gerçek klasör geri konur, dosya
baytlarının birebir aynı olduğu doğrulanır ve kullanıcının uygulaması yeniden başlatılır. "Windows ile başlat" kaydına
dokunulmaz. Güncelleme bildirimi, sürüm yayımlandıktan sonra gerçek GitHub'a karşı denenir (çalışan sürüm geçici olarak
daha düşük numarayla derlenmiş bir kopyayla).

## İnceleme sonrası eklenenler

Bağımsız inceleme ve uçtan uca denemelerde çıkan durumlar için:

- **Eski sürüm açılırsa:** 1.0.0 biçim 2 dosyayı okuyamaz; dosyayı (ve biçim 2 `.bak`'ı) `*.okunamadi-*` adıyla kenara
  alıp boş açılır ya da biçim 1 `.bak`'a döner. 1.1.0 açılışta bu sürümün okuyabildiği en yeni kenar dosyasını, açılan
  dosyadan yeniyse geri getirir; yerine geçtiği dosya `sifreler.dat.onceki-yyyyMMdd-HHmmss` olarak saklanır ve açılış
  uyarısı ne olduğunu söyler ("Eski exe'yi silebilirsin").
- **Windows ile başlat kaydı eski ya da silinmiş bir exe'yi gösteriyorsa** açılışta çalışan exe'ye taşınır (yükseltmeden
  sonra oturum açılışında eski sürümün başlamasını önler). Daha yeni bir sürümü gösteren kayda dokunulmaz.
- **İki satırlık yapıştırma:** Windows'un tek satırlık kutusu yapıştırılan metni ilk satırda keser; kullanıcı adı + şifre
  birlikte şifre kutusuna yapıştırılınca şifre olarak kullanıcı adı kaydedilirdi. Kutular sonu kırpıldıktan sonra birden
  fazla satır içeren yapıştırmayı reddeder: "Ad, kullanıcı adı ve şifre tek satır olmalı".
- **Güncelleme kontrolü başarısız olursa** (oturum açılışında ağ geç gelebilir) 24 saat yerine 15 dakika sonra yeniden
  denenir.
