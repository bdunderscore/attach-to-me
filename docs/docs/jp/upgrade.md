# 更新ガイド

## 1.1.x → 1.2

1.1.xから1.2にアップグレードする場合は、そのまま1.1.1のunitypackageを上書きインポートしてください。

**1.2からUdonSharpの1.0βに対応しています！**
* U#をアップグレードする前に、Attach-To-Meを1.2にアップグレードすることを強く推奨します。順番を間違えた場合は`Attachables Controller`を一度手動でシーンから削除してください。

## 1.1 → 1.1.1

1.1から1.1.1にアップグレードする場合は、そのまま1.1.1のunitypackageを上書きインポートしてください。

### 1.1.1での主な更新点

* Udonのバグを迂回する調整

## 1.0 → 1.1 （または1.1.1）

1.0から1.1に更新する場合は以下の手順おを踏んでください

* [UdonSharp](https://github.com/MerlinVR/UdonSharp/releases) が0.20かそれ以前のバージョンの場合はまず0.20.1か、それ以降のバージョンに更新してください。
* 1.1のunitypackageを上書きインポートしてください

### 1.1での主な更新点

* Quest対応させました！なお、Udonの負荷のため、同時にトラッキングするオブジェクトは10個ぐらいに抑えることをお勧めします（初代Questで計測）
* アタッチオブジェクトをプレハブ化した時の安定性を向上
* 大量のオブジェクトがトラッキングするときの負荷軽減のため、手に持った時のトラッキングをObjectSyncから独自の方式に変更。
* 指向性はマーカーからの+Z方向のみを優先するようになります（以前は+Zと-Z両方でした）
* ボーン選択が同時に近くにいる複数のプレイヤーのボーンを考慮するようになり、操作がより分かりやすくなりました。
* 「自分を優先」オプションを削除（複数人が同時に選択候補に入っていると必要性が下がるため）
* Global controllerの検索はシーン読み込み時自動的に行われるようになりました。これでうっかりとControllerを削除再設置などをしても、オブジェクトをすべてをいじる必要がありません。