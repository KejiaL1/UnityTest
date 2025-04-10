using UnityEngine;
using UnityEngine.UI;

public class ButtonSound : MonoBehaviour
{
    public AudioClip clickSound; // ��ť�����Ч
    private AudioSource audioSource;

    void Start()
    {
        // �ڵ�ǰ��������� AudioSource ���
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false; // ����������һ��ʼ�Ͳ���
        audioSource.clip = clickSound; // ��ֵ��Ч
    }

    public void PlayClickSound()
    {
        if (audioSource != null && clickSound != null)
        {
            audioSource.PlayOneShot(clickSound);
        }
    }
}
