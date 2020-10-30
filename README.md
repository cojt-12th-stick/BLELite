# BLELite

Windows 環境で Unity とデバイスで BLE 通信をするためのモジュール。  
BLE 関連は、WinRT を呼んでいるため、Unity からは直接使えない。  
そのため、Unity とのデータのやり取りに共有メモリを用いた。

共有メモリ周りは、https://github.com/sh-akira/UnityMemoryMappedFile を導入。

## How to Use

Unity 上で https://connect.unity.com/p/blueooth-low-energy-unity-asset-for-ios-and-android
の `Initialize` を実行前に BLELite を起動する。

Editor で使う場合は、Unity Editor 拡張で`PlayMode` が変更された際に、`Process.Start("BLELite.exe");`とすれば、問題ない。

また、Stand Alone でもアプリ起動時に同様に行えば、同様に利用できる。
