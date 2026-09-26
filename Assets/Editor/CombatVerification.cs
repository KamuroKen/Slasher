using System;
using System.Collections;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CombatVerification
{
    const string Report="Library/CombatVerification.txt";
    static CombatVerification()
    {
        EditorApplication.playModeStateChanged += Changed;

    }
    [MenuItem("Tools/Combat/Run Test Room Verification")]
    public static void Run()
    {
        if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var scene = SceneManager.GetActiveScene();
        if (scene.path != "Assets/Scenes/testroom.unity" || scene.isDirty)
        {
            Debug.LogWarning("Open and save testroom before running combat verification.");
            return;
        }
        SessionState.SetBool("CombatVerification.Requested", true);
        File.WriteAllText(Report,"START\n");
        EditorApplication.isPlaying=true;
    }
    static void Changed(PlayModeStateChange state)
    {
        if(state==PlayModeStateChange.EnteredPlayMode && SessionState.GetBool("CombatVerification.Requested", false))
        {
            SessionState.EraseBool("CombatVerification.Requested");
            new GameObject("Temporary combat verification").AddComponent<CombatVerificationRunner>();
        }
    }
}
public sealed class CombatVerificationRunner : MonoBehaviour
{
    const string Report="Library/CombatVerification.txt";
    Keyboard keyboard;
    double deadline;
    void Awake()
    {
        deadline = EditorApplication.timeSinceStartup + 60;
        EditorApplication.update += Watchdog;
    }
    void Watchdog()
    {
        if (EditorApplication.timeSinceStartup <= deadline) return;
        File.AppendAllText(Report, "FAIL: verification timed out.\n");
        Cleanup();
    }
    void OnDestroy()
    {
        EditorApplication.update -= Watchdog;
        if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
    }
    IEnumerator Start()
    {
        var run=Verify();
        while(true)
        {
            object next;
            try { if(!run.MoveNext()) break; next=run.Current; }
            catch(Exception e) { File.AppendAllText(Report,"FAIL: "+e+"\n"); Cleanup(); yield break; }
            yield return next;
        }
        File.AppendAllText(Report,"ALL PASSED\n"); Cleanup();
    }
    void Cleanup()
    {
        EditorApplication.update -= Watchdog;
        if(keyboard!=null && keyboard.added) InputSystem.RemoveDevice(keyboard);
        EditorApplication.isPlaying=false;
    }
    void Check(bool ok,string message) { if(!ok) throw new Exception(message); File.AppendAllText(Report,"PASS: "+message+"\n"); }
    void Keys(params Key[] keys) { InputSystem.QueueStateEvent(keyboard,new KeyboardState(keys)); }
    IEnumerator Verify()
    {
        yield return null;
        keyboard=InputSystem.AddDevice<Keyboard>();
        var p=GameObject.FindGameObjectWithTag("Player");
        var ph=p.GetComponent<PlayerHealth>(); var pm=p.GetComponent<PlayerMovement>(); var attack=p.GetComponent<PlayerAttack>();
        var pb=p.GetComponent<Rigidbody2D>(); var pa=p.GetComponent<Animator>();
        var bat=FindFirstObjectByType<EnemyController>(); var bb=bat.GetComponent<Rigidbody2D>(); var bh=bat.GetComponent<Damageable>();
        var arena = FindFirstObjectByType<ArenaBounds>();
        Check(arena != null && attack != null && p.GetComponent<PlayerHealthDisplay>() != null, "Refactored components wired in scene");
        Check(arena.Clamp(new Vector2(30, -30), Vector2.one * .5f) == new Vector2(15.5f, -9.5f), "Shared bounds preserve actor inset");
        Check(arena.Clamp(new Vector2(30, -30), new Vector2(40, 40)) == Vector2.zero, "Camera centers when view is larger than arena");
        Check(ph!=null && ph.Health.CurrentHealth==100,"Player health wired at 100 HP");
        Check(Mathf.Abs(bb.position.x-pb.position.x-7)<.01f && bat.transform.parent.name=="Enemies","Bat prefab placed seven cells right under Enemies");
        yield return new WaitForSeconds(.2f);
        Check(bat.State==EnemyController.BehaviourState.Idle,"Bat idle outside detection radius");
        pb.position=new Vector2(2,0); yield return new WaitForSeconds(.2f);
        Check(bat.State==EnemyController.BehaviourState.Chase && bb.position.x<7,"Bat detects and chases within six cells");
        bb.position=pb.position+Vector2.right*.85f;
        yield return new WaitForSeconds(.12f);
        Check(bat.State==EnemyController.BehaviourState.Attack && ph.Health.CurrentHealth==100,"Attack windup gives warning before damage");
        bool playerFlashed=false,playerHurt=false;
        ph.Health.Damaged += source => { var block=new MaterialPropertyBlock(); p.GetComponent<SpriteRenderer>().GetPropertyBlock(block); playerFlashed=block.GetFloat("_FlashAmount")==1; pa.Update(0); playerHurt=pa.GetCurrentAnimatorStateInfo(0).IsName("Hurt_"+pm.Facing); };
        Vector2 before=pb.position;
        yield return new WaitForSeconds(.26f);
        Check(ph.Health.CurrentHealth==90 && playerFlashed && playerHurt,"Bat hit deals 10, triggers directional Hurt and white flash");
        Check(Vector2.Distance(before,pb.position)<.02f,"Player takes hit without knockback");
        Check(!ph.Health.TryTakeDamage(10,bb.position),"Player invulnerability rejects immediate second hit");
        bb.position=new Vector2(14,8); yield return new WaitForSeconds(.7f);
        Check(ph.Health.CurrentHealth==90,"One Bat strike damages only once");
        Keys(Key.D); yield return new WaitForSeconds(.06f); Keys(); yield return null;
        bool batFlashed=false;
        bh.Damaged += source=> { var block=new MaterialPropertyBlock(); bat.GetComponent<SpriteRenderer>().GetPropertyBlock(block); batFlashed=block.GetFloat("_FlashAmount")==1; };
        for(int swing=0;swing<3;swing++)
        {
            pb.position=Vector2.zero; bb.position=new Vector2(1.2f,0); bb.linearVelocity=Vector2.zero;
            Keys(Key.Space); yield return null; Keys();
            yield return new WaitForSeconds(.42f);
            Check(bh.CurrentHealth==40-swing*20,"Sword swing "+(swing+1)+" deals exactly 20 damage");
            if(swing<2) Check(batFlashed,"Bat hit flashes white");
            else Check(bat.State==EnemyController.BehaviourState.Dead && !bb.simulated && !bat.GetComponent<Collider2D>().enabled,"Third sword hit starts death and disables enemy collision");
            yield return new WaitForSeconds(.4f);
        }
        yield return new WaitForSeconds(.35f); Check(bat==null,"Bat despawns after Death animation");
        pb.position = new Vector2(15.49f, 9.49f);
        Keys(Key.D, Key.W); yield return new WaitForSeconds(.2f); Keys(); yield return null;
        Check(pb.position.x <= 15.501f && pb.position.y <= 9.501f, "Player movement respects shared room boundary");
        var camera = Camera.main;
        Vector2 cameraInset = new Vector2(camera.orthographicSize * camera.aspect, camera.orthographicSize);
        Check(Vector2.Distance(camera.transform.position, arena.Clamp(pb.position, cameraInset)) < .02f, "Camera uses shared bounds and its own viewport inset");
        pb.position = Vector2.zero;
        Key[] directions={Key.W,Key.A,Key.S,Key.D}; string[] names={"Up","Left","Down","Right"};
        for(int i=0;i<4;i++)
        {
            Keys(directions[i]); yield return new WaitForSeconds(.05f); Keys(); yield return null;
            Check(pm.Facing==names[i],"Movement faces "+names[i]);
            Check(ph.Health.TryTakeDamage(1,pb.position+Vector2.up),"Damage accepted after immunity");
            yield return null;
            Check(pa.GetCurrentAnimatorStateInfo(0).IsName("Hurt_"+names[i]),"Hurt animation faces "+names[i]);
            yield return new WaitForSeconds(.55f);
            Check(pa.GetCurrentAnimatorStateInfo(0).IsName("Idle_"+names[i]),"Hurt returns to idle "+names[i]);
        }
        Keys(Key.Space); yield return new WaitForSeconds(.05f); Keys();
        Check(attack.IsAttacking, "Attack active before nonlethal interruption");
        Check(ph.Health.TryTakeDamage(1, Vector2.up), "Nonlethal hit accepted during sword attack");
        Check(!attack.IsAttacking, "Hurt cancels sword attack immediately");
        yield return new WaitForSeconds(.55f);
        var renderer = p.GetComponent<SpriteRenderer>();
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        Check(properties.GetFloat("_FlashAmount") == 0 && renderer.color.a == 1f, "Flash and blink restore renderer after immunity");
        renderer.color = Color.green;
        yield return null;
        Check(renderer.color == Color.green, "Idle hit-flash component leaves other color changes untouched");
        renderer.color = Color.white;
        Keys(Key.Space); yield return new WaitForSeconds(.05f); Keys();
        Check(attack.IsAttacking,"Sword started before fatal hit");
        Check(ph.Health.TryTakeDamage(1000,pb.position+Vector2.left),"Fatal damage accepted");
        yield return null;
        Check(ph.Health.IsDead && !pm.enabled && !attack.IsAttacking && !pb.simulated && pa.enabled,"Death cancels attack and movement while Animator continues");
        Check(pa.GetCurrentAnimatorStateInfo(0).IsName("Death_Right"),"Death uses last facing direction");
        yield return new WaitForSeconds(1.35f);
        Check(p.GetComponent<SpriteRenderer>().sprite.name=="PlayerDeath_Right_11","Death completes and holds final frame");
        Check(!ph.Health.TryTakeDamage(1,Vector2.zero),"Dead player rejects further damage");
    }
}
