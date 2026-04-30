# Firestore /products 시드 업로더

13개 IAP 상품을 `(default)` Firestore 의 `/products` 컬렉션에 일괄 등록.

## 1회 셋업

1. Node.js 18+ 설치
2. `firebase/seed/` 안에서:
   ```
   npm install
   ```
3. Firebase Console > 톱니바퀴 > **프로젝트 설정 > 서비스 계정** > "새 비공개 키 생성"
   → 다운로드된 JSON 을 **`firebase/seed/service-account.json`** 으로 저장 (`.gitignore` 됨)

## 실행

```
npm run dry-run   # 콘솔 출력만, 업로드 안 함
npm run seed      # 실제 업로드
```

옵션:
```
node seed.js --collection products
node seed.js --service-account /path/to/key.json
```

## 결과 확인

Firebase Console > Firestore Database > `(default)` > `products` 컬렉션 → 13개 도큐먼트:
- `xyz.aimed.balloonloop.coin.1000` ~ `coin.100000` (6개)
- `xyz.aimed.balloonloop.bundle.tier1` ~ `tier5` (5개)
- `xyz.aimed.balloonloop.noads`
- `xyz.aimed.balloonloop.offer.starter`

도큐먼트 ID = full Store SKU. 같은 ID 로 재실행하면 덮어쓰기 (멱등).

## 보안

- `service-account.json` 은 절대 커밋 금지 (`.gitignore` 등록됨). 키 유출 시 Console > 서비스 계정 > 키 무효화
- `package-lock.json` 은 커밋 OK
