namespace RadioHomeEngine

module Discovery =
    let getDriveInfoForDevice device = {
        device = device
        disc = {
            audio = DiscDriveStatus.tryGetAudioDiscInfo device
            data = DataCD.tryGetDataDiscInfo device
        }
    }

    let getDriveInfo scope =
        scope
        |> DiscDrives.getDevices
        |> List.map getDriveInfoForDevice
