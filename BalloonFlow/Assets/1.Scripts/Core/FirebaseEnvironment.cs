using Firebase;
using Firebase.Firestore;

namespace BalloonFlow
{
    /// <summary>
    /// Firebase 환경 설정. dev/prod 분리 시 BuildConfig + Scripting Define Symbol 로 확장.
    /// 현재는 단일 named database "test" 사용. prod 시점에 "(default)" 또는 별도 Firebase 프로젝트로 전환.
    /// </summary>
    public static class FirebaseEnvironment
    {
        /// <summary>
        /// Firestore named database 이름. 빈 문자열이면 "(default)" 사용.
        /// Firebase Unity SDK 13.10.0 의 named database 라우팅이 불완전한 것으로 보여 (default) 로 통일.
        /// dev/prod 분리는 별도 Firebase 프로젝트로 진행 (named database 미사용).
        /// </summary>
        public const string FirestoreDatabaseName = "";

        /// <summary>
        /// 현재 환경에 맞는 Firestore 인스턴스. DatabaseName 이 비어있으면 default, 아니면 named.
        /// </summary>
        public static FirebaseFirestore GetFirestore()
        {
            if (string.IsNullOrEmpty(FirestoreDatabaseName))
                return FirebaseFirestore.DefaultInstance;
            return FirebaseFirestore.GetInstance(FirebaseApp.DefaultInstance, FirestoreDatabaseName);
        }
    }
}
