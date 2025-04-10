using UnityEngine;
using UnityEngine.UI;

public class PlayAndControlUI : MonoBehaviour
{
    [Header("Canvas Settings")]
    public Canvas canvas1;   // 包含 Play 按钮的 Canvas（主菜单）
    public Canvas canvas2;   // 包含控制按钮的 Canvas（游戏控制界面）

    [Header("Play Button")]
    public Button playButton;  // 主菜单中的 Play 按钮

    [Header("Control Buttons")]
    public Button increaseSizeButton;   // 放大按钮
    public Button decreaseSizeButton;   // 缩小按钮
    public Button rotateLeftButton;     // 左旋转按钮
    public Button rotateRightButton;    // 右旋转按钮
    public Button moveLeftButton;       // 左平移按钮
    public Button moveRightButton;      // 右平移按钮
    public Button resetButton;          // 还原按钮

    [Header("Return Button")]
    public Button returnButton;         // 返回主菜单按钮（位于 Canvas2）

    [Header("Target Object")]
    public GameObject targetObject;     // 需要被控制的父对象

    // 保存目标对象初始状态（用于还原）
    private Vector3 originalPosition;
    private Vector3 originalScale;
    private Quaternion originalRotation;
    // 保存整体初始底部的 Y 坐标（计算所有子 Renderer）
    private float originalBottom;

    private void Start()
    {
        if (targetObject != null)
        {
            originalPosition = targetObject.transform.position;
            originalScale = targetObject.transform.localScale;
            originalRotation = targetObject.transform.rotation;

            // 计算所有子 Renderer 的最小 Y 值
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
                Debug.LogWarning("在 targetObject 或其子对象中没有找到 Renderer 组件！");
            }
        }
        else
        {
            Debug.LogWarning("请在 Inspector 中指定 targetObject！");
        }

        // 初始时确保 Canvas2 隐藏（控制按钮所在的 Canvas）
        if (canvas2 != null)
            canvas2.gameObject.SetActive(false);

        // 绑定 Play 按钮事件（进入游戏界面）
        if (playButton != null)
        {
            playButton.onClick.AddListener(OnPlayButtonClicked);
        }
        else
        {
            Debug.LogWarning("请在 Inspector 中指定 Play Button！");
        }

        // 绑定控制按钮事件
        if (increaseSizeButton != null) increaseSizeButton.onClick.AddListener(IncreaseSize);
        if (decreaseSizeButton != null) decreaseSizeButton.onClick.AddListener(DecreaseSize);
        if (rotateLeftButton != null) rotateLeftButton.onClick.AddListener(RotateLeft);
        if (rotateRightButton != null) rotateRightButton.onClick.AddListener(RotateRight);
        if (moveLeftButton != null) moveLeftButton.onClick.AddListener(MoveLeft);
        if (moveRightButton != null) moveRightButton.onClick.AddListener(MoveRight);
        if (resetButton != null) resetButton.onClick.AddListener(ResetObject);

        // 绑定返回按钮事件（返回主菜单）
        if (returnButton != null)
        {
            returnButton.onClick.AddListener(ReturnToMainMenu);
        }
        else
        {
            Debug.LogWarning("请在 Inspector 中指定返回主菜单按钮！");
        }
    }

    /// <summary>
    /// 点击 Play 按钮后触发，隐藏 Canvas1 并显示 Canvas2
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
    /// 返回主菜单：隐藏 Canvas2，显示 Canvas1
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
    /// 放大目标对象，并调整位置使底部高度不变
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
    /// 缩小目标对象，并调整位置使底部高度不变
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
    /// 调整目标对象的位置，使整体底部保持在原始高度
    /// </summary>
    private void AdjustBottom()
    {
        if (targetObject != null)
        {
            Renderer[] renderers = targetObject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("未找到任何 Renderer 组件！");
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

    /// <summary>向左旋转目标对象</summary>
    private void RotateLeft()
    {
        if (targetObject != null)
        {
            targetObject.transform.Rotate(0f, -15f, 0f);
        }
    }

    /// <summary>向右旋转目标对象</summary>
    private void RotateRight()
    {
        if (targetObject != null)
        {
            targetObject.transform.Rotate(0f, 15f, 0f);
        }
    }

    /// <summary>
    /// 以目标对象自身坐标系向左平移
    /// </summary>
    private void MoveLeft()
    {
        if (targetObject != null)
        {
            targetObject.transform.Translate(Vector3.left * 0.5f, Space.Self);
        }
    }

    /// <summary>
    /// 以目标对象自身坐标系向右平移
    /// </summary>
    private void MoveRight()
    {
        if (targetObject != null)
        {
            targetObject.transform.Translate(Vector3.right * 0.5f, Space.Self);
        }
    }

    /// <summary>
    /// 还原目标对象到初始状态
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
