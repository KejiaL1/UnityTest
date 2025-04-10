using UnityEngine;
using UnityEngine.UI;

public class ButtonSound : MonoBehaviour
{
    public AudioClip clickSound; // 按钮点击音效
    private AudioSource audioSource;

    void Start()
    {
        // 在当前对象上添加 AudioSource 组件
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false; // 让它不会在一开始就播放
        audioSource.clip = clickSound; // 赋值音效
    }

    public void PlayClickSound()
    {
        if (audioSource != null && clickSound != null)
        {
            audioSource.PlayOneShot(clickSound);
        }
    }
}
