using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    public float accelerationForce = 800f; // 加速力度
    public float brakingForce = 1200f;       // 刹车力度
    public float maxSpeed = 20f;             // 最大车速
    public float turnTorque = 300f;          // 转向扭矩

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        // 确保没有冻结 Y 轴旋转，否则转向不起作用
        // 如果在 Inspector 中冻结了 Y 轴旋转，请取消勾选
    }

    void FixedUpdate()
    {
        // 获取垂直和水平方向的输入
        float moveInput = Input.GetAxis("Vertical");     // W/S 键控制前进/倒退
        float turnInput = Input.GetAxis("Horizontal");     // A/D 键控制转向

        // 控制前进/后退
        if (Mathf.Abs(moveInput) > 0.1f)
        {
            // 防止超速
            if (rb.velocity.magnitude < maxSpeed)
            {
                float force = moveInput >= 0 ? accelerationForce : brakingForce;
                rb.AddForce(transform.forward * moveInput * force * Time.fixedDeltaTime, ForceMode.Acceleration);
            }
            else if (moveInput < 0)
            {
                rb.AddForce(transform.forward * moveInput * brakingForce * Time.fixedDeltaTime, ForceMode.Acceleration);
            }
        }

        // 始终根据水平输入施加转向扭矩
        if (Mathf.Abs(turnInput) > 0.1f)
        {
            rb.AddTorque(Vector3.up * turnInput * turnTorque * Time.fixedDeltaTime, ForceMode.Acceleration);
        }
    }
}
