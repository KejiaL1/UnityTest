using UnityEngine;
using UnityEngine.UI;

public class PlayAndControlUI : MonoBehaviour
{
    [Header("Canvas Settings")]
    public Canvas canvas1;   // ���� Play ��ť�� Canvas�����˵���
    public Canvas canvas2;   // �������ư�ť�� Canvas����Ϸ���ƽ��棩

    [Header("Play Button")]
    public Button playButton;  // ���˵��е� Play ��ť

    [Header("Control Buttons")]
    public Button increaseSizeButton;   // �Ŵ�ť
    public Button decreaseSizeButton;   // ��С��ť
    public Button rotateLeftButton;     // ����ת��ť
    public Button rotateRightButton;    // ����ת��ť
    public Button moveLeftButton;       // ��ƽ�ư�ť
    public Button moveRightButton;      // ��ƽ�ư�ť
    public Button resetButton;          // ��ԭ��ť

    [Header("Return Button")]
    public Button returnButton;         // �������˵���ť��λ�� Canvas2��

    [Header("Target Object")]
    public GameObject targetObject;     // ��Ҫ�����Ƶĸ�����

    // ����Ŀ������ʼ״̬�����ڻ�ԭ��
    private Vector3 originalPosition;
    private Vector3 originalScale;
    private Quaternion originalRotation;
    // ���������ʼ�ײ��� Y ���꣨���������� Renderer��
    private float originalBottom;

    private void Start()
    {
        if (targetObject != null)
        {
            originalPosition = targetObject.transform.position;
            originalScale = targetObject.transform.localScale;
            originalRotation = targetObject.transform.rotation;

            // ���������� Renderer ����С Y ֵ
            Renderer[] renderers = targetObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length > 0)
            {
                float bottom = float.MaxValue;
                foreach (Renderer rend in renderers)
                {
                    bottom = Mathf.Min(bottom, rend.bounds.min.y);
                }
                originalBottom = bottom;
            }
            else
            {
                Debug.LogWarning("�� targetObject �����Ӷ�����û���ҵ� Renderer �����");
            }
        }
        else
        {
            Debug.LogWarning("���� Inspector ��ָ�� targetObject��");
        }

        // ��ʼʱȷ�� Canvas2 ���أ����ư�ť���ڵ� Canvas��
        if (canvas2 != null)
            canvas2.gameObject.SetActive(false);

        // �� Play ��ť�¼���������Ϸ���棩
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonClicked);
        }
        else
        {
            Debug.LogWarning("���� Inspector ��ָ�� Play Button��");
        }

        // �󶨿��ư�ť�¼�
        if (increaseSizeButton != null) increaseSizeButton.onClick.AddListener(IncreaseSize);
        if (decreaseSizeButton != null) decreaseSizeButton.onClick.AddListener(DecreaseSize);
        if (rotateLeftButton != null) rotateLeftButton.onClick.AddListener(RotateLeft);
        if (rotateRightButton != null) rotateRightButton.onClick.AddListener(RotateRight);
        if (moveLeftButton != null) moveLeftButton.onClick.AddListener(MoveLeft);
        if (moveRightButton != null) moveRightButton.onClick.AddListener(MoveRight);
        if (resetButton != null) resetButton.onClick.AddListener(ResetObject);

        // �󶨷��ذ�ť�¼����������˵���
        if (returnButton != null)
        {
            returnButton.onClick.AddListener(ReturnToMainMenu);
        }
        else
        {
            Debug.LogWarning("���� Inspector ��ָ���������˵���ť��");
        }
    }

    /// <summary>
    /// ��� Play ��ť�󴥷������� Canvas1 ����ʾ Canvas2
    /// </summary>
    private void OnPlayButtonClicked()
    {
        Debug.Log("Play button clicked. Switching canvases.");
        if (canvas1 != null)
            canvas1.gameObject.SetActive(false);
        if (canvas2 != null)
            canvas2.gameObject.SetActive(true);
    }

    /// <summary>
    /// �������˵������� Canvas2����ʾ Canvas1
    /// </summary>
    private void ReturnToMainMenu()
    {
        Debug.Log("Return button clicked. Going back to main menu.");
        if (canvas2 != null)
            canvas2.gameObject.SetActive(false);
        if (canvas1 != null)
            canvas1.gameObject.SetActive(true);
    }

    /// <summary>
    /// �Ŵ�Ŀ����󣬲�����λ��ʹ�ײ��߶Ȳ���
    /// </summary>
    private void IncreaseSize()
    {
        if (targetObject != null)
        {
            targetObject.transform.localScale *= 1.1f;
            AdjustBottom();
        }
    }

    /// <summary>
    /// ��СĿ����󣬲�����λ��ʹ�ײ��߶Ȳ���
    /// </summary>
    private void DecreaseSize()
    {
        if (targetObject != null)
        {
            targetObject.transform.localScale *= 0.9f;
            AdjustBottom();
        }
    }

    /// <summary>
    /// ����Ŀ������λ�ã�ʹ����ײ�������ԭʼ�߶�
    /// </summary>
    private void AdjustBottom()
    {
        if (targetObject != null)
        {
            Renderer[] renderers = targetObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("δ�ҵ��κ� Renderer �����");
                return;
            }

            float currentBottom = float.MaxValue;
            foreach (Renderer rend in renderers)
            {
                currentBottom = Mathf.Min(currentBottom, rend.bounds.min.y);
            }

            float offsetY = originalBottom - currentBottom;
            targetObject.transform.position += new Vector3(0, offsetY, 0);
        }
    }

    /// <summary>������תĿ�����</summary>
    private void RotateLeft()
    {
        if (targetObject != null)
        {
            targetObject.transform.Rotate(0f, -15f, 0f);
        }
    }

    /// <summary>������תĿ�����</summary>
    private void RotateRight()
    {
        if (targetObject != null)
        {
            targetObject.transform.Rotate(0f, 15f, 0f);
        }
    }

    /// <summary>
    /// ��Ŀ�������������ϵ����ƽ��
    /// </summary>
    private void MoveLeft()
    {
        if (targetObject != null)
        {
            targetObject.transform.Translate(Vector3.left * 0.5f, Space.Self);
        }
    }

    /// <summary>
    /// ��Ŀ�������������ϵ����ƽ��
    /// </summary>
    private void MoveRight()
    {
        if (targetObject != null)
        {
            targetObject.transform.Translate(Vector3.right * 0.5f, Space.Self);
        }
    }

    /// <summary>
    /// ��ԭĿ����󵽳�ʼ״̬
    /// </summary>
    private void ResetObject()
    {
        if (targetObject != null)
        {
            targetObject.transform.position = originalPosition;
            targetObject.transform.localScale = originalScale;
            targetObject.transform.rotation = originalRotation;
        }
    }
}
