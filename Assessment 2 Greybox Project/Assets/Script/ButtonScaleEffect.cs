using UnityEngine;
using UnityEngine.EventSystems;

public class ButtonScaleEffect : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public Vector3 hoverScale = new Vector3(1.2f, 1.2f, 1.2f); // ��ͣʱ�Ĵ�С
    private Vector3 originalScale;

    void Start()
    {
        originalScale = transform.localScale; // ��¼ԭʼ��С
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        transform.localScale = hoverScale; // ��ͣʱ�Ŵ�
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        transform.localScale = originalScale; // �ָ�ԭʼ��С
    }
}
