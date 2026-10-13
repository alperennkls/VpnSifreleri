<p align="center"><img src="docs/icon.png" width="96" alt=""></p>

<h1 align="center">VPN Şifreleri</h1>

<p align="center">
VPN şifrelerini ve SAP kullanıcı adı + şifrelerini sistem tepsisinden tek tıkla panoya kopyalayan küçük bir Windows uygulaması.
</p>

<p align="center">
<a href="https://github.com/alperennkls/VpnSifreleri/releases/latest"><b>⬇ İndir (VpnSifreleri.exe)</b></a>
</p>

---

Saatin yanındaki yeşil kilit ikonuna **sağ tıkla** ve kayıt adına tıkla: şifre panoya kopyalanır, `Ctrl+V` ile yapıştırırsın. Kayıtlar Türkçe alfabeye göre sıralıdır.

## Kurulum

1. [Releases](https://github.com/alperennkls/VpnSifreleri/releases/latest) sayfasından `VpnSifreleri.exe`'yi indir.
2. Kalıcı bir klasöre koy (ör. `Belgeler\VpnSifreleri`) ve çalıştır. İlk açılışta kayıt penceresi açılır.
3. Bilgisayar açılınca kendiliğinden başlasın istersen menüden **Windows ile başlat**'ı işaretle.

> [!NOTE]
> Exe kod imzalı olmadığı için ilk açılışta **"Windows kişisel bilgisayarınızı korudu"** uyarısı çıkabilir. **Ek bilgi → Yine de çalıştır** ile açabilirsin.

## Kullanım

- **Kayıt eklemek:** menüden **Şifreleri yönet…** (ya da ikona çift tık). Ad, isteğe bağlı kullanıcı adı ve şifreyi yazıp `Enter`.
- **VPN şifresi:** kullanıcı adını boş bırak; tıklayınca yalnızca şifre kopyalanır.
- **SAP girişi:** kullanıcı adını da yaz; tıklayınca iki satır kopyalanır (üstte kullanıcı adı, altta şifre). SAP giriş ekranında kullanıcı kutusuna yapıştırınca ikisi de yerine geçer. Kayıt eklerken kullanıcı adını ve şifreyi ayrı kutulara yapıştır; iki satır birden tek kutuya yapıştırılmaz.
- Kopyalama bildiriminde yalnızca kayıt adı görünür. Kopyalanan metin Win+V pano geçmişine girmez ve 30 saniye sonra panodan silinir.
- Güncellemek ya da silmek için kayıt penceresinde listeden seç.

## Güncelleme

Yeni sürüm çıkınca uygulama bildirim gösterir ve menüde **Yeni sürüm var** belirir; tıklayınca indirme sayfası açılır.

1. Menüden **Çıkış** ile uygulamayı kapat.
2. Yeni `VpnSifreleri.exe`'yi eskisinin **üzerine** kaydet (aynı klasör, aynı ad). Başka bir yere indirdiysen eski exe'yi sil; **Windows ile başlat** açıksa yeni sürüm ilk açılışta onu kendine taşır.
3. Aç. Kayıtların olduğu gibi gelir.

Güncelledikten sonra eski exe'yi açma: eski sürüm yeni kayıt dosyasını okuyamaz ve kenara alır. Yanlışlıkla açıldıysa onu menüden kapatıp yeni sürümü aç; kenara alınan kayıtları kendisi geri yükler ve bunu söyler.

## Gizlilik

- Kayıtlar yalnızca bu bilgisayarda, `%APPDATA%\VpnSifreleri\sifreler.dat` dosyasında, Windows oturumunla şifrelenmiş (DPAPI) olarak durur. Ana şifre yoktur; dosya başka bir bilgisayarda ya da başka bir Windows kullanıcısında açılmaz. Bu yüzden şifrelerinin bir yedeğini ayrıca tut.
- Uygulama internete yalnızca GitHub'a son sürüm numarasını sormak için çıkar (açılıştan kısa süre sonra ve günde bir). Kayıtlarla ilgili hiçbir bilgi gönderilmez.

## Kaynaktan derleme

Windows 10/11'de ek kurulum gerekmez; Windows'la gelen .NET Framework derleyicisi kullanılır:

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Önce testler çalışır; geçerse `VpnSifreleri.exe` üretilir.

## Lisans

[MIT](LICENSE)
