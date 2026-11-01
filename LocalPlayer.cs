using UnityEngine;

namespace TomYum
{
    /// <summary>First-person movement. Touch: left side = stick, right side = look. Desktop: WASD + mouse, E/F/G.</summary>
    public class LocalPlayer : MonoBehaviour
    {
        public GameManager gm;
        public CharacterController cc;
        public Transform cam;
        public float yaw = 180f;
        public float pitch;

        public Vector2 stickStart, stickCur;
        public bool StickActive { get { return stickId >= 0; } }

        float vy;
        int stickId = -1, lookId = -1;

        void Update()
        {
            float dt = Time.deltaTime;
            Vector2 mv = Vector2.zero, look = Vector2.zero;

            if (gm.CanControl)
            {
                if (Application.isMobilePlatform) ReadTouch(out mv, out look);
                else
                {
                    mv = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
                    if (Cursor.lockState == CursorLockMode.Locked)
                        look = new Vector2(Input.GetAxis("Mouse X"), -Input.GetAxis("Mouse Y")) * 2.2f;
                    if (Input.GetKeyDown(KeyCode.E) || Input.GetMouseButtonDown(0)) gm.DoGrab();
                    if (Input.GetKeyDown(KeyCode.F) || Input.GetMouseButtonDown(1)) gm.DoUse();
                    if (Input.GetKeyDown(KeyCode.G)) gm.DoThrow();
                }
            }
            else
            {
                stickId = -1;
                lookId = -1;
            }

            yaw += look.x;
            pitch = Mathf.Clamp(pitch + look.y, -80f, 80f);

            Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * new Vector3(mv.x, 0f, mv.y) * 3.6f;
            if (cc.isGrounded) vy = -1f; else vy -= 20f * dt;
            dir.y = vy;
            cc.Move(dir * dt);

            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            cam.localRotation = Quaternion.Euler(pitch, 0f, 0f);
        }

        void ReadTouch(out Vector2 mv, out Vector2 look)
        {
            mv = Vector2.zero;
            look = Vector2.zero;
            float u = Screen.height / 540f;

            foreach (Touch t in Input.touches)
            {
                Vector2 gp = new Vector2(t.position.x, Screen.height - t.position.y);

                if (t.phase == TouchPhase.Began)
                {
                    if (gm.UiBlocks(gp)) continue;
                    if (gp.x < Screen.width * 0.4f && stickId < 0)
                    {
                        stickId = t.fingerId;
                        stickStart = gp;
                        stickCur = gp;
                    }
                    else if (gp.x >= Screen.width * 0.4f && lookId < 0)
                    {
                        lookId = t.fingerId;
                    }
                }

                if (t.fingerId == stickId)
                {
                    stickCur = gp;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) stickId = -1;
                }
                else if (t.fingerId == lookId)
                {
                    look += new Vector2(t.deltaPosition.x, -t.deltaPosition.y) * 0.15f;
                    if (t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled) lookId = -1;
                }
            }

            if (stickId >= 0)
            {
                Vector2 d = (stickCur - stickStart) / (70f * u);
                d = Vector2.ClampMagnitude(d, 1f);
                mv = new Vector2(d.x, -d.y); // screen y goes down
            }
        }
    }
}
