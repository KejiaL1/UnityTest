using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    public float accelerationForce = 800f; // ��������
    public float brakingForce = 1200f;       // ɲ������
    public float maxSpeed = 20f;             // �����
    public float turnTorque = 300f;          // ת��Ť��

    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        // ȷ��û�ж��� Y ����ת������ת��������
        // ����� Inspector �ж����� Y ����ת����ȡ����ѡ
    }

    void FixedUpdate()
    {
        // ��ȡ��ֱ��ˮƽ���������
        float moveInput = Input.GetAxis("Vertical");     // W/S ������ǰ��/����
        float turnInput = Input.GetAxis("Horizontal");     // A/D ������ת��

        // ����ǰ��/����
        if (Mathf.Abs(moveInput) > 0.1f)
        {
            // ��ֹ����
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

        // ʼ�ո���ˮƽ����ʩ��ת��Ť��
        if (Mathf.Abs(turnInput) > 0.1f)
        {
            rb.AddTorque(Vector3.up * turnInput * turnTorque * Time.fixedDeltaTime, ForceMode.Acceleration);
        }
    }
}
