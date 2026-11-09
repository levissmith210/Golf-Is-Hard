using System.Collections;
using UnityEngine;

public class WinScreenController : MonoBehaviour
{
    public Animator animator;
    public float noInputTime = 5f;
    public float endAnimTime = 2f;

    private bool startPressed;
    private float startTime;
    private GUIStyle titleStyle;
    private GUIStyle subtitleStyle;
    private GUIStyle badgeStyle;
    private GUIStyle buttonStyle;
    private GUIStyle footerStyle;

    private void Start()
    {
        startTime = Time.time;
    }

    public void Update()
    {
        if (!startPressed && Input.anyKey && (Time.time - startTime) > noInputTime)
            BeginFinish();
    }

    private void OnGUI()
    {
        if (startPressed)
            return;

        EnsureStyles();
        int previousDepth = GUI.depth;
        GUI.depth = -100;

        FrutigerAeroUI.DrawSkyBackdrop();

        float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.65f, 1.12f);
        float panelWidth = Mathf.Min(680f * scale, Screen.width - 32f);
        float panelHeight = Mathf.Min(410f * scale, Screen.height - 32f);
        Rect panel = new Rect((Screen.width - panelWidth) * 0.5f, (Screen.height - panelHeight) * 0.5f,
            panelWidth, panelHeight);

        FrutigerAeroUI.DrawGlassPanel(panel, FrutigerAeroUI.Cyan,
            new Color(0.84f, 0.97f, 1f, 0.9f), 0.2f);
        FrutigerAeroUI.DrawGolfBall(new Vector2(panel.x + 74f * scale, panel.y + 92f * scale), 48f * scale);
        FrutigerAeroUI.DrawGolfBall(new Vector2(panel.xMax - 74f * scale, panel.y + 92f * scale), 48f * scale);
        FrutigerAeroUI.DrawCircle(new Rect(panel.center.x - 48f * scale, panel.y + 27f * scale,
            22f * scale, 22f * scale), new Color(0.73f, 0.92f, 0.3f, 0.9f));
        FrutigerAeroUI.DrawCircle(new Rect(panel.center.x + 26f * scale, panel.y + 39f * scale,
            14f * scale, 14f * scale), new Color(0.17f, 0.73f, 0.94f, 0.75f));

        badgeStyle.fontSize = Mathf.RoundToInt(14f * scale);
        titleStyle.fontSize = Mathf.RoundToInt(43f * scale);
        subtitleStyle.fontSize = Mathf.RoundToInt(19f * scale);
        buttonStyle.fontSize = Mathf.RoundToInt(20f * scale);
        footerStyle.fontSize = Mathf.RoundToInt(14f * scale);

        FrutigerAeroUI.DrawPill(new Rect(panel.center.x - 102f * scale, panel.y + 70f * scale,
            204f * scale, 27f * scale), new Color(0.27f, 0.78f, 0.43f, 0.84f));
        GUI.Label(new Rect(panel.center.x - 98f * scale, panel.y + 70f * scale,
            196f * scale, 27f * scale), "NINE HOLES COMPLETE", badgeStyle);
        GUI.Label(new Rect(panel.x + 18f, panel.y + 106f * scale, panel.width - 36f, 70f * scale),
            "Course Complete!", titleStyle);
        GUI.Label(new Rect(panel.x + 32f, panel.y + 174f * scale, panel.width - 64f, 47f * scale),
            "You made the whole course look easy.", subtitleStyle);

        FrutigerAeroUI.DrawPill(new Rect(panel.x + panel.width * 0.2f, panel.y + 230f * scale,
            panel.width * 0.6f, 3f * scale), new Color(0.1f, 0.65f, 0.84f, 0.3f));

        if (Time.time - startTime > noInputTime)
        {
            float buttonWidth = Mathf.Min(270f * scale, panel.width - 70f);
            Rect finishButton = new Rect(panel.center.x - buttonWidth * 0.5f,
                panel.y + 250f * scale, buttonWidth, 58f * scale);
            if (FrutigerAeroUI.DrawGlassButton(finishButton, "FINISH", buttonStyle, FrutigerAeroUI.Green, true))
                BeginFinish();

            GUI.Label(new Rect(panel.x + 20f, panel.yMax - 46f * scale, panel.width - 40f, 22f * scale),
                "PRESS ANY BUTTON TO FINISH", footerStyle);
        }
        else
        {
            GUI.Label(new Rect(panel.x + 20f, panel.y + 260f * scale, panel.width - 40f, 48f * scale),
                "ENJOY THE VICTORY LAP", footerStyle);
        }

        GUI.depth = previousDepth;
    }

    private void BeginFinish()
    {
        if (startPressed || (Time.time - startTime) <= noInputTime)
            return;

        startPressed = true;
        StartCoroutine(EndGameEnumerator());
    }

    private IEnumerator EndGameEnumerator()
    {
        if (animator != null)
            animator.SetTrigger("begin");

        yield return new WaitForSeconds(endAnimTime);
        Debug.Log("Quit!");
        Application.Quit();
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;

        titleStyle = FrutigerAeroUI.CreateStyle(43, FrutigerAeroUI.Ink);
        subtitleStyle = FrutigerAeroUI.CreateStyle(19, new Color(0.11f, 0.42f, 0.55f, 1f), TextAnchor.MiddleCenter, false);
        badgeStyle = FrutigerAeroUI.CreateStyle(14, Color.white);
        buttonStyle = FrutigerAeroUI.CreateStyle(20, FrutigerAeroUI.Ink);
        footerStyle = FrutigerAeroUI.CreateStyle(14, new Color(0.17f, 0.45f, 0.57f, 1f));
    }
}

