using System;
using System.Collections.Generic;
using UnityEngine;

namespace EndlessSurvival.Save
{
    [Serializable]
    public class SaveData
    {
        public string saveTimestamp;
        public string gameVersion = "1.0";

        [Header("World & Seed")]
        public string masterSeed;
        public int resolvedSeed;
        public float currentHour; // DayNightCycle saati

        [Header("Progress & Statistics")]
        public float totalDistanceMeters;
        public int enemiesKilled;
        public int itemsLooted;
        public int campsDiscovered;
        public int ambushesSurvived;
        public float realTimePlayed;

        [Header("Player State")]
        public Vector3 playerPosition;
        public Quaternion playerRotation;
        public float playerHealth;
        public bool playerIsBleeding;
        public float playerHunger;
        public float playerThirst;
        public float playerBladder;
        public int playerMoney;
        public int playerLooseAmmo;
        public List<SavedItemSlot> playerInventory = new List<SavedItemSlot>();

        [Header("Vehicle State")]
        public bool hasVehicle;
        public bool isPlayerInsideVehicle;
        public Vector3 vehiclePosition;
        public Quaternion vehicleRotation;
        public float vehicleFuel;
        public float vehicleHealth;
        public List<SavedItemSlot> vehicleStorage = new List<SavedItemSlot>(); // Araç Bagajı / Stash
        public List<string> installedVehicleMods = new List<string>(); // Takılı modifikasyonlar

        [Header("Story & Lore")]
        public List<string> readNoteIds = new List<string>();

        [Header("Camping")]
        public List<SavedCampfire> placedCampfires = new List<SavedCampfire>();
    }

    [Serializable]
    public class SavedItemSlot
    {
        public string itemName;
        public int amount;

        public SavedItemSlot() { }

        public SavedItemSlot(string name, int count)
        {
            itemName = name;
            amount = count;
        }
    }

    [Serializable]
    public class SavedCampfire
    {
        public Vector3 position;
        public float remainingBurnTime;

        public SavedCampfire() { }

        public SavedCampfire(Vector3 pos, float burnTime)
        {
            position = pos;
            remainingBurnTime = burnTime;
        }
    }
}
