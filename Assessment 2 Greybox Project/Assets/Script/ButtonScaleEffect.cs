using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonScaleEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Vector3 hoverScale = new Vector3(1.2f, 1.2f, 1.2f); // 悬停时的大小
    private Vector3 originalScale;

    void Start()
    {
        originalScale = transform.localScale; // 记录原始大小
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.localScale = hoverScale; // 悬停时放大
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = originalScale; // 恢复原始大小
    }
}
