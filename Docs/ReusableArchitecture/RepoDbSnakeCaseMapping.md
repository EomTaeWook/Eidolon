# RepoDB snake_case 매핑 적용 가이드

> 카탈로그: [재사용 아키텍처](README.md)
> 문서 종류: 단일 아키텍처 가이드
> 프로젝트별 표기: `[PROJECT-SPECIFIC]` 적용 프로젝트에서 확정해야 하는 항목

이 문서는 C# 모델의 PascalCase 속성을 MySQL의 snake_case 컬럼에 자동으로 연결하는 방식을 다른 프로젝트에 옮기기 위한 가이드다.

## 전달 단위

이 문서 전체를 전달한다. 실제 패키지 버전, 확장 메서드 위치, 모델 어셈블리와 실행 프로세스 등록 위치는 대상 프로젝트 적용 문서에서 확정한다.

## 적용 대상

- RepoDB를 사용한다.
- C# 속성은 PascalCase, DB 컬럼은 snake_case를 사용한다.
- `[Table]` 모델을 어셈블리 단위로 일괄 등록하려 한다.

RepoDB를 사용하지 않거나 모델별 명시적 컬럼 매핑을 기준으로 삼는 프로젝트에는 적용하지 않는다.

## 적용 결과

| C# 속성 | MySQL 컬럼 |
|---|---|
| `Id` | `id` |
| `UserId` | `user_id` |
| `CreatedAtUtc` | `created_at_utc` |
| `IsEmailVerified` | `is_email_verified` |

테이블 이름은 자동 변환하지 않는다. 모델에 RepoDB가 사용하는 `[Table]` 특성을 붙여 실제 테이블 이름을 지정한다.

```csharp
using System.ComponentModel.DataAnnotations.Schema;

namespace Sample.Database.Model
{
    [Table("tb_user")]
    public class User
    {
        public long Id { get; set; }
        public string LoginId { get; set; } = string.Empty;
        public DateTime CreatedAtUtc { get; set; }
    }
}
```

위 모델은 별도의 `[Map]` 특성 없이 `Id` → `id`, `LoginId` → `login_id`, `CreatedAtUtc` → `created_at_utc`로 매핑된다.

## 필요한 패키지

아래 조합은 이 가이드를 작성할 때 검증한 기준 버전이다. 대상 프로젝트는 현재 사용 중인 버전을 우선하고 API 호환성을 확인한다.

```xml
<ItemGroup>
  <PackageReference Include="RepoDb" Version="1.15.1" />
  <PackageReference Include="RepoDb.MySqlConnector" Version="1.15.0" />
</ItemGroup>
```

다른 버전을 사용하면 `PropertyMapper.Add`의 오버로드가 달라졌는지 확인한다.

## 1. 문자열 변환 확장 메서드

`StringExtensions.cs`를 공용 Database 프로젝트에 추가한다.

```csharp
using System.Text;

namespace Sample.Database.Core.Extensions
{
    public static class StringExtensions
    {
        public static string ToSnakeCase(this string value)
        {
            if (string.IsNullOrEmpty(value) == true)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var character = value[i];
                if (char.IsUpper(character) == true)
                {
                    if (i > 0)
                    {
                        builder.Append('_');
                    }
                    builder.Append(char.ToLowerInvariant(character));
                    continue;
                }

                builder.Append(character);
            }

            return builder.ToString();
        }
    }
}
```

이 변환기는 `UserId`처럼 단어별로 대문자를 사용하는 속성을 전제로 한다. `URLValue`는 `u_r_l_value`가 되므로 약어도 `UrlValue`처럼 작성한다. 숫자는 그대로 유지하므로 `Season2Id`는 `season2_id`가 된다.

## 2. RepoDB 속성 매핑 확장 메서드

RepoDB는 런타임 `Type`을 직접 받는 `PropertyMapper.Add`를 제공하지 않는다. 모델 어셈블리를 일괄 등록할 때는 제네릭 메서드를 리플렉션으로 찾아 호출한다.

```csharp
using RepoDb;
using System.Reflection;

namespace Sample.Database.Core.Extensions
{
    public static class RepoDbExtensions
    {
        public static void Configuration(Action<GlobalConfiguration> action)
        {
            var configuration = GlobalConfiguration.Setup().UseMySqlConnector();
            action(configuration);
        }

        public static GlobalConfiguration UseSnakeCaseMappingFor<T>(this GlobalConfiguration globalConfiguration)
            where T : class
        {
            return UseSnakeCaseMappingFor(globalConfiguration, typeof(T));
        }

        public static GlobalConfiguration UseSnakeCaseMappingFor(this GlobalConfiguration globalConfiguration,
            Type type)
        {
            var addMethod = typeof(PropertyMapper)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(method => method.Name == nameof(PropertyMapper.Add)
                    && method.IsGenericMethodDefinition
                    && method.GetParameters().Length == 3
                    && method.GetParameters()[0].ParameterType == typeof(string)
                    && method.GetParameters()[1].ParameterType == typeof(string)
                    && method.GetParameters()[2].ParameterType == typeof(bool));

            var genericAddMethod = addMethod.MakeGenericMethod(type);

            foreach (var propertyInfo in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                var columnName = propertyInfo.Name.ToSnakeCase();
                genericAddMethod.Invoke(null,
                [
                    propertyInfo.Name,
                    columnName,
                    true
                ]);
            }

            return globalConfiguration;
        }
    }
}
```

세 번째 인자인 `true`는 기존 매핑이 있어도 지정한 매핑으로 강제 등록한다는 의미다. 특정 속성에 별도 매핑이 필요하면 자동 등록 이후 해당 속성의 매핑을 다시 지정하거나 자동 등록 대상에서 제외하는 정책을 정한다.

## 3. 시작 시 모델 일괄 등록

DB 쿼리가 실행되기 전에 한 번 등록한다. `[Table]`이 붙은 클래스만 대상으로 제한해 DTO나 요청 모델이 실수로 등록되지 않게 한다.

```csharp
using Sample.Database.Core.Extensions;
using Sample.Database.Model;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

RepoDbExtensions.Configuration(configuration =>
{
    foreach (var modelType in typeof(User).Assembly.GetTypes())
    {
        if (modelType.GetCustomAttribute<TableAttribute>() == null)
        {
            continue;
        }

        configuration.UseSnakeCaseMappingFor(modelType);
    }
});
```

등록 순서는 다음과 같다.

1. `GlobalConfiguration.Setup().UseMySqlConnector()`를 호출한다.
2. DB 모델 어셈블리에서 `[Table]` 모델을 찾는다.
3. 각 public 인스턴스 속성을 snake_case 컬럼으로 등록한다.
4. Repository와 DB 연결 풀을 생성하고 쿼리를 실행한다.

API, 메시지 Worker와 배치 프로그램처럼 실행 프로세스가 여러 개면 각 프로세스가 시작될 때 각각 등록해야 한다. RepoDB 전역 설정은 다른 프로세스와 공유되지 않는다. 요청마다 매핑을 다시 등록하지 않는다.

## 호스트 프로젝트 적용 위치

- 문자열 변환 확장 위치: `[PROJECT-SPECIFIC]`
- RepoDB 매핑 확장 위치: `[PROJECT-SPECIFIC]`
- 실행 프로세스별 등록 위치: `[PROJECT-SPECIFIC]`
- `[Table]` 모델 어셈블리: `[PROJECT-SPECIFIC]`

DB를 사용하는 실행 프로세스마다 첫 쿼리 전에 같은 초기화를 적용한다. 대상 프로젝트의 API, 서버, Worker와 배치 프로세스 목록은 적용 문서에 기록한다.

## 주의사항

### 컬럼 이름만 변환한다

이 설정은 속성명과 컬럼명의 연결만 처리한다. MySQL 값의 자료형은 바꾸지 않는다. MySQL `TINYINT`가 드라이버에서 `SByte`로 반환되는데 C# 코드가 직접 `Int32`로 캐스팅하면 다음과 같은 오류가 별도로 발생할 수 있다.

```text
Unable to cast object of type 'System.SByte' to type 'System.Int32'.
```

DB 컬럼 타입, C# 속성 타입과 직접 캐스팅 코드를 서로 맞춰야 한다.

### 약어 이름을 확인한다

현재 변환은 대문자마다 `_`를 추가한다.

- 권장: `ApiUrl` → `api_url`, `UserId` → `user_id`
- 비권장: `APIURL` → `a_p_i_u_r_l`

연속 대문자를 하나의 단어로 취급하도록 알고리즘을 바꾸면 기존 DB 컬럼과 호환되는지 먼저 확인한다.

### 모델 속성과 DDL을 함께 변경한다

C# 속성을 추가하면 컬럼명은 자동 계산되지만 실제 MySQL 컬럼이 생성되지는 않는다. DDL 또는 마이그레이션에 같은 snake_case 컬럼을 추가한다.

## 확인 체크리스트

- RepoDB와 DB Connector 패키지를 설치했다.
- DB 모델에 `[Table("실제_테이블명")]`을 지정했다.
- 애플리케이션 시작 시 snake_case 매핑을 등록했다.
- 등록 코드는 첫 DB 쿼리보다 먼저 실행된다.
- 속성의 약어를 `Id`, `Url`, `Api` 형식으로 작성했다.
- C# 속성 타입과 MySQL 컬럼 타입이 호환된다.
- DB를 사용하는 모든 실행 프로세스에 같은 초기화를 적용했다.
