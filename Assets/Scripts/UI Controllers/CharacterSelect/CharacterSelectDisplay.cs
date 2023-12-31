using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class CharacterSelectDisplay : NetworkBehaviour {
    [SerializeField] private CharacterDatabase characterDatabase;
    [SerializeField] private Transform charactersHolder;
    [SerializeField] private CharacterSelectButton selectButtonPrefab;
    [SerializeField] private PlayerCard[] playerCards;
    [SerializeField] private GameObject characterInfoPanel;
    [SerializeField] private TMP_Text characterNameText;
    [SerializeField] private Transform introSpawnPoint;
    [SerializeField] private TMP_Text joinCodeText;
    [SerializeField] private Button lockInButton;

    private GameObject introInstance;
    private List<CharacterSelectButton> characterSelectButtons = new();

    private NetworkList<CharacterSelectState> players;

    private void Awake() {
        players = new NetworkList<CharacterSelectState>();
    }

    public override void OnNetworkSpawn() {
        if (IsClient) {
            Character[] allCharacters = characterDatabase.GetAllCharacters();

            foreach (var character in allCharacters) {
                var selectButtonInstance = Instantiate(selectButtonPrefab, charactersHolder);
                selectButtonInstance.SetCharacter(this, character);
                characterSelectButtons.Add(selectButtonInstance);
            }

            players.OnListChanged += HandlePlayersStateChanged;
        }

        if (IsServer) {
            NetworkManager.Singleton.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback += HandleClientDisconnected;

            foreach (NetworkClient client in NetworkManager.Singleton.ConnectedClientsList) {
                HandleClientConnected(client.ClientId);
            }
        }

        if (IsHost) {
            joinCodeText.text = $"Lobby Code: {HostManager.Instance.JoinCode}";
        }
    }

    public override void OnNetworkDespawn() {
        if (IsClient) {
            players.OnListChanged -= HandlePlayersStateChanged;
        }

        if (IsServer) {
            NetworkManager.Singleton.OnClientConnectedCallback -= HandleClientConnected;
            NetworkManager.Singleton.OnClientDisconnectCallback -= HandleClientDisconnected;
        }
    }

    private void HandleClientConnected(ulong clientId) {
        players.Add(new CharacterSelectState(clientId));
    }

    private void HandleClientDisconnected(ulong clientId) {
        foreach (var player in players) {
            if (player.ClientId == clientId) {
                players.Remove(player);
                break;
            }
        }
    }

    public void SelectCharacter(Character character) {
        foreach (var player in players) {
            if (player.ClientId != NetworkManager.Singleton.LocalClientId) continue;

            if (player.IsLockedIn) return;

            if (player.CharacterId == character.Id) return;

            if (IsCharacterTaken(character.Id)) return;
        }

        characterNameText.text = character.DisplayName;

        characterInfoPanel.SetActive(true);

        if (introInstance != null) {
            Destroy(introInstance);
        }

        introInstance = Instantiate(character.IntroPrefab, introSpawnPoint);

        SelectServerRpc(character.Id);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SelectServerRpc(int characterId, ServerRpcParams serverRpcParams = default) {
        for (int i = 0; i < players.Count; i++) {
            if (players[i].ClientId != serverRpcParams.Receive.SenderClientId) continue;

            if (!characterDatabase.IsValidCharacterId(characterId)) return;

            if (IsCharacterTaken(characterId, true)) return;

            players[i] = new CharacterSelectState(players[i].ClientId, characterId, players[i].IsLockedIn);
        }
    }

    public void LockIn() {
        LockInServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void LockInServerRpc(ServerRpcParams serverRpcParams = default) {
        for (int i = 0; i < players.Count; i++) {
            if (players[i].ClientId != serverRpcParams.Receive.SenderClientId) continue;

            if (!characterDatabase.IsValidCharacterId(players[i].CharacterId)) return;

            if (IsCharacterTaken(players[i].CharacterId, true)) return;

            players[i] = new CharacterSelectState(players[i].ClientId, players[i].CharacterId, true);
        }

        foreach (var player in players) {
            if (!player.IsLockedIn) return;
        }

        foreach (var player in players) {
            HostManager.Instance.SetCharacter(player.ClientId, player.CharacterId);
        }

        HostManager.Instance.StartGame();
    }

    private void HandlePlayersStateChanged(NetworkListEvent<CharacterSelectState> changeEvent) {
        for (int i = 0; i < playerCards.Length; i++) {
            if (players.Count > i) {
                playerCards[i].gameObject.SetActive(true);
                playerCards[i].UpdateDisplay(players[i]);
            } else {
                playerCards[i].DisableDisplay();
            }
        }

        foreach(var button in characterSelectButtons) {
            if (button.IsDisabled) continue;

            if (IsCharacterTaken(button.Character.Id)) {
                button.SetDisabled();
            }
        }

        foreach(var player in players) {
            if (player.ClientId != NetworkManager.Singleton.LocalClientId) continue;

            if (player.IsLockedIn) {
                lockInButton.interactable = false;
                break;
            }

            if (IsCharacterTaken(player.CharacterId)) {
                lockInButton.interactable = false;
                break;
            }

            lockInButton.interactable = true;
            break;
        }
    }

    private bool IsCharacterTaken(int characterId, bool checkAll = false) {
        foreach (var player in players) {
            if (!checkAll) {
                if (player.ClientId == NetworkManager.Singleton.LocalClientId) continue;
            }

            if (player.IsLockedIn && player.CharacterId == characterId) return true;
        }

        return false;
    }
}
