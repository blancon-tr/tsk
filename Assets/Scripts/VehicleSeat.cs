using UnityEngine;

/// <summary>
/// My Summer Car style Vehicle Seat:
/// When the player presses Enter while looking at the seat/steering/tiller zone,
/// Driving Mode locks the player AT THEIR EXACT CURRENT POSITION relative to the vehicle
/// (just like My Summer Car — no forced teleport snap!).
/// </summary>
public class VehicleSeat : MonoBehaviour
{
    [Header("Araç Kökü & İniş Referansı")]
    [Tooltip("Oyuncunun SetParent yapılacağı araç kök objesi (boş bırakılırsa parent alınır).")]
    public Transform vehicleRoot;
    [Tooltip("Opsiyonel sabit koltuk noktası (sadece lockAtCurrentPosition kapalıysa kullanılır).")]
    public Transform seatAnchor;
    [Tooltip("Opsiyonel iniş noktası (sadece teleportToExitPoint açıksa kullanılır).")]
    public Transform exitPoint;

    [Header("My Summer Car Oturma Davranışı")]
    [Tooltip("AÇIK (My Summer Car tarzı): Enter'a bastığın tam konum ve yükseklikten aracı kullanmaya başlarsın!")]
    public bool lockAtCurrentPosition = true;
    [Tooltip("Kapalıysa inerken de bulunduğun yerden kalkarsın; açıksa ExitPoint'e ışınlar.")]
    public bool teleportToExitPoint = false;

    [Header("İleri Uzanma (Q / Sağ Tık)")]
    public Vector3 leanForwardOffset = new Vector3(0f, -0.04f, 0.44f);
    public float leanSmoothSpeed = 10f;

    [Header("Kapı Kontrolü (Opsiyonel — Arabalar İçin)")]
    public bool requireDoorOpenToExit = false;
    public HingeInteractable exitDoor;
    [Range(10f, 90f)] public float minDoorOpenAngle = 28f;

    [Header("Etkileşim Etiketi")]
    public string vehicleName = "Karadeniz Teknesi";

    public bool IsOccupied { get; private set; }
    public FirstPersonController CurrentDriver { get; private set; }

    public Transform GetAttachParent()
    {
        if (vehicleRoot != null) return vehicleRoot;
        if (transform.parent != null) return transform.parent;
        return transform;
    }

    public bool CanExit(out string blockReason)
    {
        if (requireDoorOpenToExit && exitDoor != null)
        {
            float openAmount = Mathf.Abs(exitDoor.CurrentAngle - exitDoor.closedAngle);
            if (openAmount < minDoorOpenAngle)
            {
                blockReason = "İnmek için önce kapıyı aç!";
                return false;
            }
        }
        blockReason = null;
        return true;
    }

    public void OnDriverEnter(FirstPersonController driver)
    {
        IsOccupied = true;
        CurrentDriver = driver;
    }

    public void OnDriverExit()
    {
        IsOccupied = false;
        CurrentDriver = null;
    }
}
