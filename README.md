# Tom Yum Kitchen (Unity, P2P co-op)

เกมทำต้มยำขาย เล่นกับเพื่อนแบบ P2P ไม่มี server (คนหนึ่งเป็น host อีกคนใส่ IP เข้าไป)

## โครงสร้าง
- `Assets/Resources/Models/*.fbx` – โมเดลสร้างด้วย Blender 5.2.3 (สคริปต์ `tools/make_models.py`)
- `Assets/Scripts/GameManager.cs` – ห้องครัว, สูตร, ออเดอร์, เครือข่าย, UI
- `Assets/Scripts/NetPeer.cs` – TCP ตรงระหว่างผู้เล่น (host listen, เพื่อนต่อเข้า IP:port)
- `Assets/Editor/BuildScript.cs` – ตั้งค่า Android + build APK
- `.github/workflows/build-android.yml` – build APK ด้วย GitHub Actions

ฉากห้องครัวถูกสร้างตอนรันเกมจากโค้ด (ไม่ต้องจัดวางใน Editor) และ `BuildScript` สร้างฉากเปล่า `Assets/Scenes/Main.unity` ให้เอง

## ลูกค้าและของตกแต่ง
- ลูกค้า 3 แบบ (`customer_0..2.fbx`) เดินเข้าจากประตูมุมขวาล่างมายืนหน้าเคาน์เตอร์เสิร์ฟ มีป้ายบอกออเดอร์และเวลาเหนือหัว เมื่อได้รับซุปจะถือชามนั่งกิน ~6 วินาที แล้วจ่ายเงิน (เข้ากระเป๋าตอนกินเสร็จ) ก่อนเดินออก ถ้าคะแนนต่ำกว่า 40 ลูกค้าปฏิเสธและเดินออกทันทีโดยไม่จ่าย
- ของตกแต่ง: โต๊ะ+เก้าอี้ ต้นไม้ โคมแดง ชั้นวางเครื่องปรุง ป้ายร้าน เคาน์เตอร์เตรียมของ
- ถ้าลูกค้าหันหลังให้เคาน์เตอร์ ให้แก้ `CustomerYawOffset` ใน GameManager.cs เป็น 180

## วิธีเล่น
1. หยิบวัตถุดิบจากกล่องสีที่ผนังด้านหลัง (GRAB) หยิบชามด้วย
2. หั่นเห็ด ตะไคร้ ข่า พริก บนเขียง (USE 5 ครั้ง)
3. USE ที่หม้อมือเปล่า = เติมน้ำ, USE ที่เตา = เปิด/ปิดไฟ
4. โยน (THROW) หรือวางของลงหม้อ รอเดือด (กุ้ง/เห็ดต้องเดือดประมาณ 8 วินาที อย่าให้ไหม้)
5. ถือชามแล้ว USE ที่หม้อ = ตักซุป แล้วเอาชามไปวางที่แผ่นเขียว

เมนูมีหน้า **YOUR IP ADDRESSES** แสดง IP ในวง LAN ของตัวเอง, ปุ่มดู IP สาธารณะ (กดเองเท่านั้นถึงจะเรียก api.ipify.org),
และฝั่ง host เห็น IP ของคนที่เข้ามา

## เชื่อมต่อ
- Wi-Fi เดียวกัน: host บอก Local IP ให้เพื่อน
- ผ่านอินเทอร์เน็ต: host ต้อง port-forward พอร์ต 7777 (TCP) หรือใช้ VPN เช่น Tailscale / ZeroTier แล้วใช้ IP ของ VPN
  (ไม่มี relay จึงไม่สามารถทะลุ NAT ของมือถือ 4G/5G ได้เอง)

## Build APK ด้วย GitHub Actions
1. สร้าง repo ใหม่ ใส่ไฟล์ทั้งหมดในโฟลเดอร์นี้ แล้ว push ขึ้น branch `main`
2. แก้ `ProjectSettings/ProjectVersion.txt` ให้เป็นเวอร์ชัน Unity 6 ที่มี image ใน game-ci (ดูรายชื่อที่ https://game.ci/docs/docker/versions)
3. ตั้ง Secrets ใน Settings > Secrets and variables > Actions: `UNITY_LICENSE`, `UNITY_EMAIL`, `UNITY_PASSWORD`
   วิธีขอไฟล์ license ของ Personal ดูที่ https://game.ci/docs/github/activation (ขั้นตอนของ Unity เปลี่ยนบ่อย ให้ยึดตามหน้านั้น)
4. Actions > Build Android APK > Run workflow แล้วโหลด artifact `TomYumKitchen-apk`

APK เซ็นด้วย debug keystore ติดตั้งลงเครื่องตัวเองได้ ถ้าจะขึ้น Play Store ต้องใส่ keystore ของตัวเองเพิ่ม

## หมายเหตุ
โค้ดยังไม่เคยถูกคอมไพล์ใน Unity จริง (เครื่องที่เขียนไม่มี Unity) ถ้าเจอ error ตอน build ส่ง log มาแก้ต่อได้
